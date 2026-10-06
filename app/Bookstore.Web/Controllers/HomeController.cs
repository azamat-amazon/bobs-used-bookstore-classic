using System.Diagnostics;
using Bookstore.Web.ViewModel;
using Bookstore.Domain.Books;
using System.Threading.Tasks;
using Bookstore.Web.ViewModel.Home;
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
    public class HomeController : Controller
    {
        private readonly IBookService bookService;
        public HomeController(IBookService bookService)
        {
            this.bookService = bookService;
        }

        public async Task<ActionResult> Index()
        {
            var books = await bookService.ListBestSellingBooksAsync(4);
            return View(new HomeIndexViewModel(books));
        }

        public ActionResult Privacy()
        {
            return View();
        }

        public ActionResult Search()
        {
            return RedirectToAction("Index", "Search");
        }

        public ActionResult Cart()
        {
            return RedirectToAction("Index", "ShoppingCart");
        }

        public ActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id });
        }
    }
}