
namespace Bookstore.Web.Areas
{
    // This class is intentionally left empty. The MVC 5 AreaRegistration pattern
    // (AreaRegistration base class, AreaRegistrationContext, UrlParameter.Optional)
    // does not exist in ASP.NET Core. Area routing is configured in Program.cs
    // and controllers use the [Area("Admin")] attribute.
    //
    // Original route: "Admin/{controller}/{action}/{id}" with default action "Index"
    // and optional id parameter, scoped to namespace "Bookstore.Web.Areas.Admin.Controllers".
    public static class AdminAreaRegistration
    {
        public const string AreaName = "Admin";
    }
}