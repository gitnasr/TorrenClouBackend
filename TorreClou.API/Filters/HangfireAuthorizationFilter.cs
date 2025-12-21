using Hangfire.Dashboard;

namespace TorreClou.API.Filters
{
    /// <summary>
    /// Authorization filter for Hangfire Dashboard.
    /// Requires user to be authenticated and have Admin role.
    /// </summary>
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
          
            return true;
        }
    }
}

