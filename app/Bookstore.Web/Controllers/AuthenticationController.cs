using System;
using BobsBookstoreClassic.Data;
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

namespace Bookstore.Web.Controllers
{
    public class AuthenticationController : Controller
    {
        public ActionResult Login(string redirectUri = null)
        {
            if (string.IsNullOrWhiteSpace(redirectUri))
                return RedirectToAction("Index", "Home");
            return Redirect(redirectUri);
        }

        public ActionResult LogOut()
        {
            return BookstoreConfiguration.GetSetting("Services/Authentication") == "aws" ? CognitoSignOut() : LocalSignOut();
        }

        private ActionResult LocalSignOut()
        {
            if (HttpContext.Request.Cookies["LocalAuthentication"] != null)
            {
                HttpContext.Response.Cookies.Delete("LocalAuthentication");
            }

            return RedirectToAction("Index", "Home");
        }

        private ActionResult CognitoSignOut()
        {
            if (Request.Cookies[".AspNet.Cookies"] != null)
            {
                Response.Cookies.Delete(".AspNet.Cookies");
            }

            var domain = BookstoreConfiguration.GetSetting("Authentication/Cognito/CognitoDomain");
            var clientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
            var logoutUri = $"{Request.Scheme}://{Request.Host.Host}:{Request.Host.Port}/";
            return Redirect($"{domain}/logout?client_id={clientId}&logout_uri={logoutUri}");
        }
    }
}