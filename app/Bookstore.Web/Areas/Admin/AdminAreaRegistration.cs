// PORT-TODO
// {"was": "System.Web.Mvc.AreaRegistration", "note": "ASP.NET Core has no AreaRegistration. Admin area controllers must use [Area(\"Admin\")] attribute and the area route must be registered in Program.cs: app.MapControllerRoute(name: \"Admin_default\", pattern: \"Admin/{controller}/{action}/{id?}\", defaults: new { action = \"Index\" }). This file can be deleted once those are in place.", "files": ["Program.cs"]}

// Original MVC 5 Admin area registration — preserved for reference.
// The route was: Admin/{controller}/{action}/{id} with default action = Index.

namespace Bookstore.Web.Areas
{
    // This class is intentionally left empty. The MVC 5 AreaRegistration base class
    // does not exist in ASP.NET Core. Area routing is now configured in Program.cs
    // and controllers use [Area("Admin")] attribute.
    public static class AdminAreaRegistration
    {
        public const string AreaName = "Admin";
    }
}