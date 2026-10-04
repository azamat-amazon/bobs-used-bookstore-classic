// PORT-TODO
// {"was": "System.Web.Mvc.AreaRegistration", "note": "ASP.NET Core has no AreaRegistration. Add [Area(\"Admin\")] to each controller in Areas/Admin/Controllers/, and register the area route in Program.cs: app.MapControllerRoute(name: \"Admin_default\", pattern: \"Admin/{controller}/{action}/{id?}\", defaults: new { action = \"Index\" }).RequireAuthorization(); — then delete this file.", "files": ["Program.cs"]}

namespace Bookstore.Web.Areas
{
    // This class is intentionally empty — AreaRegistration does not exist in ASP.NET Core.
    // The Admin area route must be registered in Program.cs via MapControllerRoute with an {area} pattern,
    // and each Admin controller must be decorated with [Area("Admin")].
}