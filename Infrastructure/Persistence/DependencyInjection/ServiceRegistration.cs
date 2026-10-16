using Application.Interface;
using Application.Interface.IRepository;
using Application.Interface.IService;
using Application.Jobs;
using Infrastructure.Jobs;
using Infrastructure.Options;
using Infrastructure.Persistence.Context;
using Infrastructure.Persistence.Interceptors;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Persistence.DependencyInjection
{
    public static partial class ServiceRegistration
    {
        public static IServiceCollection ConfigureInfrastructureService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<AuditableEntitySaveChangesInterceptor>();

            services.AddDbContext<SpmsDbContext>((serviceProvider, options) =>
                options.UseSqlServer(configuration.GetConnectionString("SpmsConnection"))
                    .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntitySaveChangesInterceptor>())
            );

            services.AddScoped<IUnitOfWork, SpmsUnitOfWork>();

services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));    
            services.AddScoped<IGoalRepository, GoalRepository>();
            services.AddScoped<IKraRepository, KraRepository>();
            services.AddScoped<IKpiRepository, KpiRepository>();
            services.AddScoped<IKpiTargetRepository, KpiTargetRepository>();
            services.AddScoped<IKpiActualValueRepository, KpiActualValueRepository>();
            services.AddScoped<IFiscalYearRepository, FiscalYearRepository>();
            services.AddScoped<IFiscalPeriodRepository, FiscalPeriodRepository>();
            services.AddScoped<IScorecardRepository, ScorecardRepository>();
            services.AddScoped<IOrganizationProfileRepository, OrganizationProfileRepository>();
            services.AddScoped<IPeriodLockConfigRepository, PeriodLockConfigRepository>();
            services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
            services.AddScoped<IUnitOfMeasureRepository, UnitOfMeasureRepository>();
            services.AddScoped<IKpiScoreSnapshotRepository, KpiScoreSnapshotRepository>();
            services.AddScoped<IKraScoreSnapshotRepository, KraScoreSnapshotRepository>();
            services.AddScoped<IGoalScoreSnapshotRepository, GoalScoreSnapshotRepository>();
            services.AddScoped<IScorecardScoreSnapshotRepository, ScorecardScoreSnapshotRepository>();
            services.AddScoped<IDivisionRepository, DivisionRepository>();
            services.AddScoped<IAuditLogEntryRepository, AuditLogEntryRepository>();
            services.AddScoped<IDivisionPlanRepository, DivisionPlanRepository>();
            services.AddScoped<IDivisionGoalRepository, DivisionGoalRepository>();
            services.AddScoped<IDivisionContributionRepository, DivisionContributionRepository>();
services.AddScoped<IDivisionScoreSnapshotRepository, DivisionScoreSnapshotRepository>();

            services.AddScoped<IInitiativeRepository, InitiativeRepository>();
            services.AddScoped<IMilestoneRepository, MilestoneRepository>();
            services.AddScoped<IInitiativeUpdateLogRepository, InitiativeUpdateLogRepository>();
            services.AddScoped<IInitiativeDependencyRepository, InitiativeDependencyRepository>();

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

            services.AddScoped<IPasswordHasher, PasswordHasherService>();

            services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
            services.AddScoped<ITokenService, JwtTokenService>();

            services.AddScoped<ISpmsJobScheduler, HangfireJobScheduler>();
            services.AddTransient<ExpiredRefreshTokenCleanupJob>();
            services.AddTransient<IScoreSnapshotRecalculationJob, ScoreSnapshotRecalculationJob>();
            services.AddTransient<ScoreSnapshotRecalculationJob>();
            services.AddTransient<IDivisionScoreRecalculationJob, DivisionScoreRecalculationJob>();
            services.AddTransient<DivisionScoreRecalculationJob>();
            services.AddTransient<ScoreSnapshotReconciliationJob>();
            services.AddScoped<ISpmsRecurringJobRegistrar, SpmsRecurringJobRegistrar>();

            return services;
        }
    }
}