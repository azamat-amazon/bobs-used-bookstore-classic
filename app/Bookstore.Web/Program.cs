using Amazon.Rekognition;
using Amazon.S3;
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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// NLog integration (replaces LoggingSetup.ConfigureLogging)
// ---------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// ---------------------------------------------------------------------------
// Configuration — BookstoreConfiguration as a singleton backed by IConfiguration
// (replaces the legacy static ConfigurationSetup.ConfigureConfiguration)
// ---------------------------------------------------------------------------
var bookstoreConfig = new BookstoreConfiguration(builder.Configuration);
builder.Services.AddSingleton(bookstoreConfig);

// Configure NLog targets based on app settings (replaces LoggingSetup.ConfigureLogging)
ConfigureNLog(bookstoreConfig);

// ---------------------------------------------------------------------------
// EF Core DbContext (replaces Autofac InstancePerRequest registration)
// ---------------------------------------------------------------------------
var connectionString = bookstoreConfig.GetConnectionString("BookstoreDatabaseConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ---------------------------------------------------------------------------
// Domain services (replaces DependencyInjectionSetup.ConfigureDependencyInjection)
// All Autofac registrations → built-in DI IServiceCollection
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IReferenceDataService, ReferenceDataService>();
builder.Services.AddScoped<IOfferService, OfferService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IAddressService, AddressService>();
builder.Services.AddScoped<IShoppingCartService, ShoppingCartService>();
builder.Services.AddScoped<IImageResizeService, Bookstore.Data.ImageResizeService.ImageResizeService>();

// Repositories
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IAddressRepository, AddressRepository>();
builder.Services.AddScoped<IBookRepository, BookRepository>();
builder.Services.AddScoped<IOfferRepository, OfferRepository>();
builder.Services.AddScoped<IShoppingCartRepository, ShoppingCartRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();

// PaginatedList (open generic, scoped — replaces Autofac InstancePerLifetimeScope)
builder.Services.AddScoped(typeof(IPaginatedList<>), typeof(PaginatedList<>));

// File service — conditional on configuration (replaces HttpRuntime.AppDomainAppPath)
if (bookstoreConfig.GetSetting("Services/FileService") == "aws")
{
    builder.Services.AddSingleton<IAmazonS3, AmazonS3Client>();
    builder.Services.AddScoped<IFileService, S3FileService>();
}
else
{
    builder.Services.AddScoped<IFileService>(sp =>
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        var webRootPath = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        return new LocalFileService(Path.Combine(webRootPath, "Content"));
    });
}

// Image validation service — conditional on configuration
if (bookstoreConfig.GetSetting("Services/ImageValidationService") == "aws")
{
    builder.Services.AddSingleton<IAmazonRekognition, AmazonRekognitionClient>();
    builder.Services.AddScoped<IImageValidationService, RekognitionImageValidationService>();
}
else
{
    builder.Services.AddScoped<IImageValidationService, LocalImageValidationService>();
}

// ---------------------------------------------------------------------------
// Authentication (replaces AuthenticationConfig.ConfigureAuthentication)
// ---------------------------------------------------------------------------
if (bookstoreConfig.GetSetting("Services/Authentication") == "aws")
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
            options.Authority = bookstoreConfig.GetSetting("Authentication/Cognito/MetadataAddress");
            options.ClientId = bookstoreConfig.GetSetting("Authentication/Cognito/LocalClientId");
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;

            // Preserve AWS Cognito claim mapping (cognito:username, cognito:groups)
            options.ClaimActions.MapJsonKey("cognito:username", "cognito:username");
            options.ClaimActions.MapJsonKey("cognito:groups", "cognito:groups");

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    // Map cognito:groups to role claims so [Authorize(Roles = "...")] works
                    var identity = context.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
                    if (identity != null)
                    {
                        var groupsClaims = identity.FindAll("cognito:groups").ToList();
                        foreach (var groupClaim in groupsClaims)
                        {
                            identity.AddClaim(new System.Security.Claims.Claim(
                                System.Security.Claims.ClaimTypes.Role, groupClaim.Value));
                        }
                    }
                    return Task.CompletedTask;
                }
            };
        });
}
else
{
    // Local authentication: cookie-based with LocalAuthenticationMiddleware
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Authentication/Login";
        });
}

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// MVC with controllers + views (replaces FilterConfig.RegisterGlobalFilters)
// ---------------------------------------------------------------------------
builder.Services.AddControllersWithViews(options =>
{
    // [PORT-NOTE] Dropped HandleErrorAttribute — ASP.NET Core uses UseExceptionHandler middleware instead.
    // [PORT-NOTE] Dropped global AuthorizeAttribute — authentication is enforced via middleware and
    // per-controller/action [Authorize]. The Admin area uses [Authorize(Roles = "Administrators")]
    // on AdminAreaControllerBase.
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware pipeline (order is load-bearing — see Microsoft docs on middleware order)
// ---------------------------------------------------------------------------

// Exception handling — replaces Application_Error from Global.asax.cs
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

// [PORT-NOTE] Dropped BundleConfig.RegisterBundles — System.Web.Optimization does not exist in
// ASP.NET Core. Static assets are served directly from wwwroot/ via UseStaticFiles.

app.UseRouting();

app.UseAuthentication();

// Local authentication middleware — runs after UseAuthentication so it can set
// the ClaimsPrincipal for the local dev flow
if (bookstoreConfig.GetSetting("Services/Authentication") != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

app.UseAuthorization();

// ---------------------------------------------------------------------------
// Routing (replaces RouteConfig.RegisterRoutes + AreaRegistration.RegisterAllAreas)
// ---------------------------------------------------------------------------

// Admin area route (replaces AdminAreaRegistration)
app.MapControllerRoute(
    name: "Admin_default",
    pattern: "Admin/{controller}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

// Default route (replaces RouteConfig default "controller/action/id")
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ---------------------------------------------------------------------------
// NLog configuration helper (replaces LoggingSetup.ConfigureLogging)
// ---------------------------------------------------------------------------
static void ConfigureNLog(BookstoreConfiguration bookstoreConfig)
{
    var config = new LoggingConfiguration();

    Target loggingTarget;

    if (bookstoreConfig.GetSetting("Services/LoggingService") == "aws")
    {
        loggingTarget = new AWSTarget { LogGroup = Constants.AppName };
    }
    else
    {
        loggingTarget = new DebuggerTarget();
    }

    config.AddTarget("logging", loggingTarget);
    config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Info, loggingTarget));

    LogManager.Configuration = config;
}
