using System;
using BobsBookstoreClassic.Data;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

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
            if (HttpContext.Request.Cookies.ContainsKey("LocalAuthentication"))
            {
                HttpContext.Response.Cookies.Delete("LocalAuthentication");
            }

            return RedirectToAction("Index", "Home");
        }

        private ActionResult CognitoSignOut()
        {
            if (Request.Cookies.ContainsKey(".AspNet.Cookies"))
            {
                Response.Cookies.Delete(".AspNet.Cookies");
            }

            var domain = BookstoreConfiguration.GetSetting("Authentication/Cognito/CognitoDomain");
            var clientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
            var logoutUri = $"{Request.Scheme}://{Request.Host}/";
            return Redirect($"{domain}/logout?client_id={clientId}&logout_uri={logoutUri}");
        }
    }
}