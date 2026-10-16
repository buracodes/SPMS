using API.Authorization;
using API.Middleware;
using API.Services;
using Application.DependencyInjection;
using Application.Interface.IService;
using Application.Options;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Domain.Constants;
using Hangfire;
using Hangfire.Dashboard;
using Infrastructure.Jobs;
using Infrastructure.Options;
using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.DependencyInjection;
using Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "AllowAngular";

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

builder.Services.ConfigureInfrastructureService(builder.Configuration);
builder.Services.AddApplicationService();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

var securityOptions = new SecurityOptions();
builder.Configuration.GetSection(SecurityOptions.SectionName).Bind(securityOptions);
builder.Services.AddSingleton(securityOptions);

var defaultAdmin = new DefaultAdminOptions();
builder.Configuration.GetSection(DefaultAdminOptions.SectionName).Bind(defaultAdmin);
builder.Services.AddSingleton(defaultAdmin);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission.Name, policy =>
            policy.Requirements.Add(new PermissionRequirement(permission.Name)));
    }
});

builder.Services.AddProblemDetails();

// Model-state failures must produce a 400, not reach the handler. With
// SuppressModelStateInvalidFilter enabled, a body that fails to bind (missing `required`
// members, malformed JSON, an empty payload) was passed to the action as null, so MediatR threw
// "Value cannot be null. (Parameter 'request')" and the client saw HTTP 500 for what is really a
// client error. MVC's automatic filter returns a ValidationProblemDetails (400) with an `errors`
// map, which is the same shape ExceptionMiddleware already emits for ValidationExceptions.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = false;
});

builder.Services.AddControllers(options =>
{
    var fallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(fallbackPolicy));
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters
   .Add(new System.Text.Json.Serialization.JsonStringEnumConverter(namingPolicy: JsonNamingPolicy.CamelCase));
});

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
#pragma warning disable AV0016
    options.AssumeDefaultVersionWhenUnspecified = true;
#pragma warning restore AV0016
    options.ReportApiVersions = true;
    options.ApiVersionReader = new QueryStringApiVersionReader("api-version");
}).AddMvc().AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
});

builder.Services.AddSwaggerGen(options =>
{
#pragma warning disable ASP0000
    var provider = builder.Services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();
#pragma warning restore ASP0000
    foreach (var description in provider.ApiVersionDescriptions)
    {
        options.SwaggerDoc(description.GroupName, new OpenApiInfo
        {
            Title = "SPMS API",
            Version = description.GroupName,
            Description = "Strategic Planning and Monitoring System API — Ethiopian Commodity Exchange"
        });
    }

    const string schemeId = "bearer";
    options.AddSecurityDefinition(schemeId, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT access token obtained from POST /api/auth/login."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(schemeId, document)] = new List<string>()
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<SpmsDbContext>("sql", HealthStatus.Unhealthy);

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSpmsSqlServerStorage(builder.Configuration.GetConnectionString("SpmsConnection")!));

builder.Services.AddHangfireServer(options =>
{
    options.SchedulePollingInterval = TimeSpan.FromSeconds(15);
});

//var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                description.GroupName);
        }
    });
}

await app.Services.InitializeDatabaseAsync();

app.Services.RegisterRecurringJobs();

app.UseHttpsRedirection();

app.UseCors(CorsPolicyName);

app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "SPMS Background Jobs",
    Authorization = new[] { new HangfireDashboardAuthorizationFilter() }
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthCheckResponseAsync
});

app.Run();

static async Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            durationMs = e.Value.Duration.TotalMilliseconds,
            description = e.Value.Description
        }),
        totalDurationMs = report.TotalDuration.TotalMilliseconds
    };

    var json = JsonSerializer.Serialize(payload);
    await context.Response.WriteAsync(json);
}