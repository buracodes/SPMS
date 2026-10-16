using Domain.Constants;
using Hangfire;
using Hangfire.Dashboard;

namespace API.Authorization
{
    public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            return httpContext.User.Identity?.IsAuthenticated == true
                && httpContext.User.IsInRole(Roles.Admin);
        }
    }
}
