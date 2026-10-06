using System.IO;
using System.Reflection;
using System.Security.Claims;
using Amazon.Rekognition;
using Amazon.S3;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using BobsBookstoreClassic.Data;
using Bookstore.Common;
using Bookstore.Data;
using Bookstore.Data.FileServices;
using Bookstore.Data.ImageResizeService;
using Bookstore.Data.ImageValidationServices;
using Bookstore.Data.Repositories;
using Bookstore.Domain;
using Bookstore.Domain.Addresses;
using Bookstore.Domain.Books;
using Bookstore.Domain.Carts;
using Bookstore.Domain.Customers;
using Bookstore.Domain.Offers;
using Bookstore.Domain.Orders;
using Bookstore.Domain.ReferenceData;
using Bookstore.Web.Helpers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging — NLog with optional AWS CloudWatch target
// (Ported from LoggingSetup.ConfigureLogging)
// ---------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Host.UseNLog();

var nlogConfig = new LoggingConfiguration();
Target loggingTarget;

if (builder.Configuration["Services:LoggingService"] == "aws")
{
    loggingTarget = new AWSTarget { LogGroup = Constants.AppName };
}
else
{
    loggingTarget = new DebuggerTarget();
}

nlogConfig.AddTarget("aws", loggingTarget);
nlogConfig.LoggingRules.Add(new NLog.Config.LoggingRule("*", NLog.LogLevel.Info, loggingTarget));
LogManager.Configuration = nlogConfig;

// ---------------------------------------------------------------------------
// Configuration — AWS Systems Manager Parameter Store
// (Ported from ConfigurationSetup.ConfigureConfiguration)
// ---------------------------------------------------------------------------
// When running against AWS, load parameters from SSM using the modern
// Amazon.Extensions.Configuration.SystemsManager provider. The SSM values
// overlay the local appsettings so keys like "ConnectionStrings:BookstoreDatabaseConnection"
// and "Authentication:Cognito:*" resolve at runtime.
if (builder.Configuration["Services:Database"] == "aws"
    || builder.Configuration["Services:Authentication"] == "aws"
    || builder.Configuration["Services:FileService"] == "aws")
{
    var rootPath = "/" + Constants.AppName;
    builder.Configuration.AddSystemsManager(rootPath, optional: true, reloadAfter: TimeSpan.FromMinutes(5));
}

// Register BookstoreConfiguration as a singleton backed by IConfiguration so the rest
// of the codebase can continue to use BookstoreConfiguration.GetSetting / GetConnectionString.
builder.Services.AddSingleton<BookstoreConfiguration>(sp =>
    new BookstoreConfiguration(sp.GetRequiredService<IConfiguration>()));

// ---------------------------------------------------------------------------
// DI — Autofac container (Ported from DependencyInjectionSetup)
// ---------------------------------------------------------------------------
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // Domain services
    containerBuilder.RegisterType<BookService>().As<IBookService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OrderService>().As<IOrderService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ReferenceDataService>().As<IReferenceDataService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OfferService>().As<IOfferService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<CustomerService>().As<ICustomerService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<AddressService>().As<IAddressService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ShoppingCartService>().As<IShoppingCartService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ImageResizeService>().As<IImageResizeService>().InstancePerLifetimeScope();

    // Repositories
    containerBuilder.RegisterType<CustomerRepository>().As<ICustomerRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<AddressRepository>().As<IAddressRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<BookRepository>().As<IBookRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OfferRepository>().As<IOfferRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ShoppingCartRepository>().As<IShoppingCartRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OrderRepository>().As<IOrderRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ReferenceDataRepository>().As<IReferenceDataRepository>().InstancePerLifetimeScope();

    // Generic paginated list
    containerBuilder.RegisterGeneric(typeof(PaginatedList<>)).As(typeof(IPaginatedList<>)).InstancePerLifetimeScope();
});

// ---------------------------------------------------------------------------
// EF Core — ApplicationDbContext
// (Ported from DependencyInjectionSetup — was Autofac RegisterType<ApplicationDbContext>)
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("BookstoreDatabaseConnection");
    options.UseSqlServer(connectionString);
});

// ---------------------------------------------------------------------------
// File service & image validation — conditional on config
// (Ported from DependencyInjectionSetup)
// ---------------------------------------------------------------------------
if (builder.Configuration["Services:FileService"] == "aws")
{
    builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
    builder.Services.AddAWSService<IAmazonS3>();
    builder.Services.AddScoped<IFileService, S3FileService>();
}
else
{
    var env = builder.Environment;
    var webRootPath = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    var contentPath = Path.Combine(webRootPath, "Content");
    builder.Services.AddSingleton<IFileService>(new LocalFileService(contentPath));
}

if (builder.Configuration["Services:ImageValidationService"] == "aws")
{
    builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
    builder.Services.AddAWSService<IAmazonRekognition>();
    builder.Services.AddScoped<IImageValidationService, RekognitionImageValidationService>();
}
else
{
    builder.Services.AddScoped<IImageValidationService, LocalImageValidationService>();
}

// ---------------------------------------------------------------------------
// Authentication — Cognito OpenIdConnect + Cookies, or local dev middleware
// (Ported from AuthenticationSetup.ConfigureAuthentication + FilterConfig)
// ---------------------------------------------------------------------------
var useAwsAuth = builder.Configuration["Services:Authentication"] == "aws";

if (useAwsAuth)
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddOpenIdConnect(options =>
        {
            options.ClientId = builder.Configuration["Authentication:Cognito:LocalClientId"];
            options.MetadataAddress = builder.Configuration["Authentication:Cognito:MetadataAddress"];
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = async context =>
                {
                    // Port of SecurityTokenValidated callback — auto-creates customer records
                    var identity = context.Principal?.Identity as ClaimsIdentity;
                    if (identity != null)
                    {
                        var customerService = context.HttpContext.RequestServices.GetRequiredService<ICustomerService>();
                        var sub = identity.FindFirst("nameidentifier")?.Value
                                  ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        var name = identity.Name;
                        var givenName = identity.FindFirst("given_name")?.Value
                                        ?? identity.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty;
                        var familyName = identity.FindFirst("family_name")?.Value
                                         ?? identity.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty;

                        if (sub != null)
                        {
                            var dto = new CreateOrUpdateCustomerDto(sub, name, givenName, familyName);
                            await customerService.CreateOrUpdateCustomerAsync(dto);
                        }
                    }
                }
            };
        });
}
else
{
    // Local authentication — no OIDC scheme; the middleware handles identity directly.
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie();
}

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// MVC — Controllers with Views + global filters
// (Ported from FilterConfig.RegisterGlobalFilters + RouteConfig)
// ---------------------------------------------------------------------------
builder.Services.AddControllersWithViews(options =>
{
    // Global [Authorize] filter — requires authentication by default on all actions.
    // Individual controllers/actions opt out via [AllowAnonymous].
    options.Filters.Add(new AuthorizeFilter());
});

builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware pipeline — order is load-bearing
// ---------------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

// Local dev authentication middleware — must run after UseAuthentication, before UseAuthorization.
// It creates a ClaimsPrincipal for the local dev user and auto-creates the customer record.
if (!useAwsAuth)
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

app.UseAuthorization();

// ---------------------------------------------------------------------------
// Application_Error → centralized exception logging
// (Ported from Global.asax.cs Application_Error)
// ---------------------------------------------------------------------------
// Note: UseExceptionHandler above handles the response; NLog integration
// captures unhandled exceptions via the logging pipeline.

// ---------------------------------------------------------------------------
// Routes — default + Admin area
// (Ported from RouteConfig.RegisterRoutes + AdminAreaRegistration)
// ---------------------------------------------------------------------------
app.MapControllerRoute(
    name: "Admin_default",
    pattern: "Admin/{controller}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ---------------------------------------------------------------------------
// Run
// ---------------------------------------------------------------------------
app.Run();
