using Bookstore.Domain.Addresses;
using Bookstore.Domain.Carts;
using Bookstore.Domain.Orders;
using Bookstore.Web.Helpers;
using Bookstore.Web.ViewModel.Checkout;
using System.Threading.Tasks;
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

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bookstore.Web.Controllers
{
    public class CheckoutController : Controller
    {
        private readonly IAddressService addressService;
        private readonly IShoppingCartService shoppingCartService;
        private readonly IOrderService orderService;
        public CheckoutController(IShoppingCartService shoppingCartService, IOrderService orderService, IAddressService addressService)
        {
            this.shoppingCartService = shoppingCartService;
            this.orderService = orderService;
            this.addressService = addressService;
        }

        public async Task<ActionResult> Index()
        {
            var shoppingCart = await shoppingCartService.GetShoppingCartAsync(HttpContext.GetShoppingCartCorrelationId());
            var addresses = await addressService.GetAddressesAsync(User.GetSub());
            return View(new CheckoutIndexViewModel(shoppingCart, addresses));
        }

        [HttpPost]
        public async Task<ActionResult> Index(CheckoutIndexViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);
            var dto = new CreateOrderDto(User.GetSub(), HttpContext.GetShoppingCartCorrelationId(), model.SelectedAddressId);
            var orderId = await orderService.CreateOrderAsync(dto);
            return RedirectToAction("Finished", new { orderId });
        }

        public async Task<ActionResult> Finished(int orderId)
        {
            var order = await orderService.GetOrderAsync(orderId);
            return View(new CheckoutFinishedViewModel(order));
        }
    }
}