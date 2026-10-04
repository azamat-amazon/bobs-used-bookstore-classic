using System.Threading.Tasks;
using Bookstore.Web.Areas.Admin.Models.Orders;
using Bookstore.Domain.Orders;
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

namespace Bookstore.Web.Areas.Admin.Controllers
{
    public class OrdersController : AdminAreaControllerBase
    {
        private readonly IOrderService orderService;
        public OrdersController(IOrderService orderService)
        {
            this.orderService = orderService;
        }

        public async Task<ActionResult> Index(OrderFilters filters, int pageIndex = 1, int pageSize = 10)
        {
            var orders = await orderService.GetOrdersAsync(filters, pageIndex, pageSize);
            return View(new OrderIndexViewModel(orders, filters));
        }

        public async Task<ActionResult> Details(int id)
        {
            var order = await orderService.GetOrderAsync(id);
            return View(new OrderDetailsViewModel(order));
        }

        [HttpPost]
        public async Task<ActionResult> Details(OrderDetailsViewModel model)
        {
            var dto = new UpdateOrderStatusDto(model.OrderId, model.SelectedOrderStatus);
            await orderService.UpdateOrderStatusAsync(dto);
            TempData["Message"] = "Order status has been updated";
            return RedirectToAction("Details", new { model.OrderId });
        }
    }
}