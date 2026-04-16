using Hangfire.Dashboard;

namespace WishHub.Api.Middleware;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // Allow access in development
        var httpContext = context.GetHttpContext();
        if (httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            return true;

        // TODO: Add proper authentication for production
        // For now, require admin role or specific header
        // return httpContext.User.IsInRole("Admin");
        
        return true; // Open for development
    }
}
