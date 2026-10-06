using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

namespace Bookstore.Web.Areas.Admin.Controllers
{
    [AllowAnonymous]
    public class ErrorController : AdminAreaControllerBase
    {
        [Route("/Error/Index/{code:int}")]
        public ActionResult Index(int code)
        {
            //var exception = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            //ViewData["Path"] = exception?.Path;
            //ViewData["StatusCode"] = code;
            return View();
        }

        [Route("/error")]
        public ActionResult Support()
        {
            //var exception = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            //ViewData["Path"] = exception?.Path;
            //var error = Problem();
            //ViewData["StatusCode"] = error.StatusCode;
            return View("~/Views/Error/Index.cshtml");
        }
    }
}