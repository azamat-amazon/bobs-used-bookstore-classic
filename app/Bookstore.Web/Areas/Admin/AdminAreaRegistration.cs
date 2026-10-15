using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Bookstore.Web.Areas
{

    /// <summary>
    /// Replaces the legacy MVC5 AreaRegistration for the Admin area.
    /// Call <see cref="MapAdminArea"/> from Program.cs endpoint routing configuration.
    /// </summary>
    public static class AdminAreaRegistration
    {
        public const string AreaName = "Admin";

        /// <summary>
        /// Registers the Admin area route. Call this in Program.cs endpoint routing:
        /// <code>AdminAreaRegistration.MapAdminArea(app);</code>
        /// </summary>
        public static void MapAdminArea(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapAreaControllerRoute(
                name: "Admin_default",
                areaName: AreaName,
                pattern: "Admin/{controller}/{action}/{id?}",
                defaults: new { action = "Index" });
        }
    }
}