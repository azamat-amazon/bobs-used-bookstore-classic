using System;
using System.Drawing;
using System.Net;
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

namespace Bookstore.Web.Helpers
{
    public static class HttpContextExtensions
    {
        public static string GetShoppingCartCorrelationId(this HttpContext context)
        {
            var CookieKey = "ShoppingCartId";
            var cookieOptions = new CookieOptions
            {
                Expires = DateTime.Now.AddYears(1),
                Path = "/"
            };
            context.Request.Cookies.TryGetValue(CookieKey, out var shoppingCartClientId);
            if (string.IsNullOrWhiteSpace(shoppingCartClientId))
            {
                shoppingCartClientId = context.User.Identity.IsAuthenticated ? context.User.GetSub() : Guid.NewGuid().ToString();
            }

            context.Response.Cookies.Append(CookieKey, shoppingCartClientId, cookieOptions);
            return shoppingCartClientId;
        }
    }
}