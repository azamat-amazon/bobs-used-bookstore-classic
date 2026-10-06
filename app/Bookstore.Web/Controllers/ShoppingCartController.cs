using System.Threading.Tasks;
using Bookstore.Web.Helpers;
using Bookstore.Domain.Customers;
using Bookstore.Domain.Carts;
using Bookstore.Web.ViewModel.ShoppingCart;
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

namespace Bookstore.Web.Controllers
{
    [AllowAnonymous]
    public class ShoppingCartController : Controller
    {
        private readonly ICustomerService customerService;
        private readonly IShoppingCartService shoppingCartService;
        public ShoppingCartController(ICustomerService customerService, IShoppingCartService shoppingCartService)
        {
            this.customerService = customerService;
            this.shoppingCartService = shoppingCartService;
        }

        public async Task<ActionResult> Index()
        {
            var shoppingCart = await shoppingCartService.GetShoppingCartAsync(HttpContext.GetShoppingCartCorrelationId());
            return View(new ShoppingCartIndexViewModel(shoppingCart));
        }

        [HttpPost]
        public async Task<ActionResult> Delete(int shoppingCartItemId)
        {
            var dto = new DeleteShoppingCartItemDto(HttpContext.GetShoppingCartCorrelationId(), shoppingCartItemId);
            await shoppingCartService.DeleteShoppingCartItemAsync(dto);
            this.SetNotification("Item removed from shopping cart.");
            return RedirectToAction(nameof(Index));
        }

        public ActionResult Error()
        {
            return View();
        }
    }
}