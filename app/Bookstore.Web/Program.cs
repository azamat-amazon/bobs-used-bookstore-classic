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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;
using NLog.Web;

// ---------------------------------------------------------------------------
// Program.cs — migrated from Global.asax(.cs) + OWIN Startup.cs +
//   App_Start/{LoggingSetup, ConfigurationSetup, DependencyInjectionSetup,
//              AuthenticationSetup, FilterConfig, RouteConfig, BundleConfig}
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// ── NLog integration (was LoggingSetup.ConfigureLogging) ────────────────
// NLog is configured programmatically here (mirroring the legacy setup) and
// wired as the ASP.NET Core logging provider.
ConfigureNLog(builder.Configuration);
builder.Host.UseNLog();

// ── BookstoreConfiguration static bridge (was ConfigurationSetup) ───────
// Initialize the legacy static accessor so existing callers of
// BookstoreConfiguration.GetSetting / GetConnectionString keep working.
BookstoreConfiguration.Initialize(builder.Configuration);

// Fetch remote settings from AWS SSM Parameter Store when running in AWS
// mode, exactly as ConfigurationSetup.ConfigureConfiguration() did.
ConfigureRemoteParameters(builder.Configuration);

// ── Autofac as DI container (was DependencyInjectionSetup) ──────────────
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // Domain services
    containerBuilder.RegisterType<BookService>().As<IBookService>();
    containerBuilder.RegisterType<OrderService>().As<IOrderService>();
    containerBuilder.RegisterType<ReferenceDataService>().As<IReferenceDataService>();
    containerBuilder.RegisterType<OfferService>().As<IOfferService>();
    containerBuilder.RegisterType<CustomerService>().As<ICustomerService>();
    containerBuilder.RegisterType<AddressService>().As<IAddressService>();
    containerBuilder.RegisterType<ShoppingCartService>().As<IShoppingCartService>();
    containerBuilder.RegisterType<ImageResizeService>().As<IImageResizeService>();

    // Repositories
    containerBuilder.RegisterType<CustomerRepository>().As<ICustomerRepository>();
    containerBuilder.RegisterType<AddressRepository>().As<IAddressRepository>();
    containerBuilder.RegisterType<BookRepository>().As<IBookRepository>();
    containerBuilder.RegisterType<OfferRepository>().As<IOfferRepository>();
    containerBuilder.RegisterType<ShoppingCartRepository>().As<IShoppingCartRepository>();
    containerBuilder.RegisterType<OrderRepository>().As<IOrderRepository>();
    containerBuilder.RegisterType<ReferenceDataRepository>().As<IReferenceDataRepository>();

    // Generic paginated list
    containerBuilder.RegisterGeneric(typeof(PaginatedList<>)).As(typeof(IPaginatedList<>)).InstancePerLifetimeScope();

    // File service — local vs S3
    if (BookstoreConfiguration.GetSetting("Services:FileService") == "aws")
    {
        containerBuilder.RegisterType<AmazonS3Client>().As<IAmazonS3>().SingleInstance();
        containerBuilder.RegisterType<S3FileService>().As<IFileService>();
    }
    else
    {
        // In ASP.NET Core, static files live under wwwroot.
        var webRootPath = Path.Combine(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "Content");
        containerBuilder.RegisterInstance(new LocalFileService(webRootPath)).As<IFileService>();
    }

    // Image validation service — local vs Rekognition
    if (BookstoreConfiguration.GetSetting("Services:ImageValidationService") == "aws")
    {
        containerBuilder.RegisterType<AmazonRekognitionClient>().As<IAmazonRekognition>().SingleInstance();
        containerBuilder.RegisterType<RekognitionImageValidationService>().As<IImageValidationService>();
    }
    else
    {
        containerBuilder.RegisterType<LocalImageValidationService>().As<IImageValidationService>();
    }

    // Local authentication middleware (only when not using Cognito)
    if (BookstoreConfiguration.GetSetting("Services:Authentication") != "aws")
    {
        containerBuilder.RegisterType<LocalAuthenticationMiddleware>();
    }
});

// ── EF Core DbContext (was registered manually via Autofac) ─────────────
var connectionString = builder.Configuration.GetConnectionString("BookstoreDatabaseConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ── MVC with global filters (was FilterConfig.RegisterGlobalFilters) ────
builder.Services.AddControllersWithViews(options =>
{
    // The legacy FilterConfig registered HandleErrorAttribute (exception
    // handling) and AuthorizeAttribute globally. HandleError is replaced by
    // UseExceptionHandler middleware below. The global [Authorize] filter
    // requires all requests to be authenticated by default; controllers/
    // actions that are public use [AllowAnonymous].
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
});

// ── HttpContext accessor (replaces HttpContext.Current) ──────────────────
builder.Services.AddHttpContextAccessor();

// ── Authentication (was AuthenticationSetup.ConfigureAuthentication) ─────
if (BookstoreConfiguration.GetSetting("Services:Authentication") == "aws")
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
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.MetadataAddress = BookstoreConfiguration.GetSetting("Authentication:Cognito:MetadataAddress");
            options.ClientId = BookstoreConfiguration.GetSetting("Authentication:Cognito:LocalClientId");
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Preserve Cognito claim types — do not remap to
                // Microsoft's XML-based ClaimTypes URIs.
                NameClaimType = "cognito:username",
                RoleClaimType = "cognito:groups"
            };

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = context =>
                {
                    // Map cognito:groups claims to ClaimTypes.Role so that
                    // [Authorize(Roles = "Administrators")] works.
                    var identity = context.Principal?.Identity as ClaimsIdentity;
                    if (identity != null)
                    {
                        var groupClaims = identity.FindAll("cognito:groups").ToList();
                        foreach (var groupClaim in groupClaims)
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Role, groupClaim.Value));
                        }
                    }
                    return Task.CompletedTask;
                }
            };
        });
}
else
{
    // Local authentication uses a custom middleware that sets claims
    // directly on HttpContext.User; we still need a default scheme
    // registered so [Authorize] / RequireAuthenticatedUser does not throw
    // "No authenticationScheme was specified".
    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie();
}

builder.Services.AddAuthorization();

// ── Session (keep available in case any controller reads Session) ────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

// ---------------------------------------------------------------------------
var app = builder.Build();
// ---------------------------------------------------------------------------

// ── Exception handling (was Application_Error) ──────────────────────────
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

// ── Static files (replaces BundleConfig — bundles are dropped) ──────────
// [PORT-NOTE] BundleConfig.RegisterBundles dropped — Web Optimization is
// unsupported on ASP.NET Core. CSS/JS are served directly from wwwroot/.
// Views that referenced @Styles.Render / @Scripts.Render must use direct
// <link>/<script> tags instead (handled during view migration).
app.UseStaticFiles();

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Local authentication middleware (non-Cognito fallback) — must run after
// UseAuthentication/UseAuthorization so the cookie scheme is available,
// but before endpoints so controllers see the populated User principal.
if (BookstoreConfiguration.GetSetting("Services:Authentication") != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

// ── Routing (was RouteConfig.RegisterRoutes + AreaRegistration) ─────────

// Admin area route (was AdminAreaRegistration)
app.MapAreaControllerRoute(
    name: "Admin_default",
    areaName: "Admin",
    pattern: "Admin/{controller=Dashboard}/{action=Index}/{id?}");

// Default route (was RouteConfig — "Default" route)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ---------------------------------------------------------------------------
// Helper: configure NLog programmatically (migrated from LoggingSetup.cs)
// ---------------------------------------------------------------------------
static void ConfigureNLog(IConfiguration configuration)
{
    var config = new LoggingConfiguration();

    Target loggingTarget;

    var loggingService = configuration.GetValue<string>("Services:LoggingService");
    if (loggingService == "aws")
    {
        loggingTarget = new AWSTarget { LogGroup = Constants.AppName };
    }
    else
    {
        loggingTarget = new DebuggerTarget();
    }

    config.AddTarget("aws", loggingTarget);
    config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Info, loggingTarget));

    LogManager.Configuration = config;
}

// ---------------------------------------------------------------------------
// Helper: fetch remote parameters from SSM (migrated from ConfigurationSetup.cs)
// ---------------------------------------------------------------------------
static void ConfigureRemoteParameters(IConfiguration configuration)
{
    var rootPath = "/" + Constants.AppName;

    const string databasePath = "/Database";
    const string authenticationPath = "/Authentication";
    const string fileServicePath = "/Files";

    if (configuration.GetValue<string>("Services:Database") == "aws")
    {
        using var client = new Amazon.SimpleSystemsManagement.AmazonSimpleSystemsManagementClient();
        var request = new Amazon.SimpleSystemsManagement.Model.GetParameterRequest
        {
            Name = $"{rootPath}{databasePath}/ConnectionStrings/BookstoreDatabaseConnection"
        };
        var response = client.GetParameterAsync(request).GetAwaiter().GetResult();
        BookstoreConfiguration.AddSetting(
            response.Parameter.Name.Replace($"{rootPath}{databasePath}/", string.Empty),
            response.Parameter.Value);
    }

    if (configuration.GetValue<string>("Services:Authentication") == "aws")
    {
        using var client = new Amazon.SimpleSystemsManagement.AmazonSimpleSystemsManagementClient();
        var request = new Amazon.SimpleSystemsManagement.Model.GetParametersByPathRequest
        {
            Path = $"{rootPath}{authenticationPath}/",
            Recursive = true
        };
        var response = client.GetParametersByPathAsync(request).GetAwaiter().GetResult();
        foreach (var parameter in response.Parameters)
        {
            BookstoreConfiguration.AddSetting(
                parameter.Name.Replace($"{rootPath}/", string.Empty),
                parameter.Value);
        }
    }

    if (configuration.GetValue<string>("Services:FileService") == "aws")
    {
        using var client = new Amazon.SimpleSystemsManagement.AmazonSimpleSystemsManagementClient();
        var request = new Amazon.SimpleSystemsManagement.Model.GetParametersByPathRequest
        {
            Path = $"{rootPath}{fileServicePath}/",
            Recursive = true
        };
        var response = client.GetParametersByPathAsync(request).GetAwaiter().GetResult();
        foreach (var parameter in response.Parameters)
        {
            BookstoreConfiguration.AddSetting(
                parameter.Name.Replace($"{rootPath}/", string.Empty),
                parameter.Value);
        }
    }
}
