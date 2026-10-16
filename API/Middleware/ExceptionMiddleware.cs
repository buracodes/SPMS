using Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net;

namespace API.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate next;
        private readonly ILogger<ExceptionMiddleware> logger;
        private readonly IProblemDetailsService problemDetailsService;

        public ExceptionMiddleware(
            RequestDelegate _next,
            ILogger<ExceptionMiddleware> _logger,
            IProblemDetailsService _problemDetailsService)
        {
            next = _next;
            logger = _logger;
            problemDetailsService = _problemDetailsService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                if (ex is BadRequestException or NotFoundException or UnauthorizedException or ForbiddenException or ValidationExceptions or PeriodLockedException or ScorecardClosedException or FiscalYearMutationException)
                {
                    logger.LogWarning(ex, ex.Message);
                }
                else
                {
                    logger.LogError(ex, ex.Message);
                }

                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var statusCode = HttpStatusCode.InternalServerError;
            string title = "An unexpected error occurred. Please try again later.";
            Dictionary<string, object?> extensions = new();

            switch (exception)
            {
                case BadRequestException ex:
                    statusCode = HttpStatusCode.BadRequest;
                    title = ex.Message;
                    break;

                case NotFoundException ex:
                    statusCode = HttpStatusCode.NotFound;
                    title = ex.Message;
                    break;

                case UnauthorizedException ex:
                    statusCode = HttpStatusCode.Unauthorized;
                    title = ex.Message;
                    break;

                case ForbiddenException ex:
                    statusCode = HttpStatusCode.Forbidden;
                    title = ex.Message;
                    break;

                case ValidationExceptions ex:
                    statusCode = HttpStatusCode.BadRequest;
                    title = "One or more validation errors occurred.";
                    extensions["errors"] = ex.Errors
                        .GroupBy(e => e.Field)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.ErrorMessage).ToArray());
                    break;

                case PeriodLockedException ex:
                    statusCode = HttpStatusCode.Conflict;
                    title = ex.Message;
                    break;

                case ScorecardClosedException ex:
                    statusCode = HttpStatusCode.Conflict;
                    title = ex.Message;
                    break;

                case FiscalYearMutationException ex:
                    statusCode = HttpStatusCode.Conflict;
                    title = ex.Message;
                    break;
            }

            context.Response.StatusCode = (int)statusCode;

            var problemDetails = new ProblemDetails
            {
                Status = (int)statusCode,
                Title = title,
                Instance = context.Request.Path
            };

            problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            foreach (var extension in extensions)
            {
                problemDetails.Extensions[extension.Key] = extension.Value;
            }

            var wrote = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problemDetails
            });

            if (!wrote)
            {
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(problemDetails));
            }
        }
    }
}