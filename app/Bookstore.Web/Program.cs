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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;
using NLog.Web;

// ---------------------------------------------------------------------------
// Program.cs — consolidated from Global.asax.cs, OWIN Startup.cs,
// AuthenticationSetup, DependencyInjectionSetup, ConfigurationSetup,
// FilterConfig, RouteConfig, BundleConfig, and LoggingSetup.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// ── NLog integration (was LoggingSetup.ConfigureLogging) ──────────────────
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// ── Configuration ─────────────────────────────────────────────────────────
// ASP.NET Core IConfiguration pipeline already reads appsettings.json and
// environment variables. For AWS production, add SSM parameter store.
if (builder.Configuration["Services/Database"] == "aws"
    || builder.Configuration["Services/Authentication"] == "aws"
    || builder.Configuration["Services/FileService"] == "aws")
{
    // PORT-TODO
    // {"note": "Add Amazon.Extensions.Configuration.SystemsManager NuGet package and call builder.Configuration.AddSystemsManager to pull SSM parameters into IConfiguration automatically, replacing the manual ConfigurationSetup.ConfigureConfiguration loop."}
    var rootPath = "/" + Constants.AppName;
    builder.Configuration.AddSystemsManager(rootPath);
}

// Initialize the legacy BookstoreConfiguration singleton so that existing
// code referencing BookstoreConfiguration.GetSetting / GetConnectionString
// continues to work while callers are incrementally migrated to IConfiguration.
BookstoreConfiguration.Initialize(builder.Configuration);

// ── Autofac DI container (was DependencyInjectionSetup) ───────────────────
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // Services
    containerBuilder.RegisterType<BookService>().As<IBookService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OrderService>().As<IOrderService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ReferenceDataService>().As<IReferenceDataService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OfferService>().As<IOfferService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<CustomerService>().As<ICustomerService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<AddressService>().As<IAddressService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ShoppingCartService>().As<IShoppingCartService>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ImageResizeService>().As<IImageResizeService>().InstancePerLifetimeScope();

    // EF6 DbContext — connection string from IConfiguration.
    var connectionString = builder.Configuration.GetConnectionString("BookstoreDatabaseConnection")!;
    containerBuilder.RegisterType<ApplicationDbContext>()
        .WithParameter("connectionString", connectionString)
        .InstancePerLifetimeScope();

    // Repositories
    containerBuilder.RegisterType<CustomerRepository>().As<ICustomerRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<AddressRepository>().As<IAddressRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<BookRepository>().As<IBookRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OfferRepository>().As<IOfferRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ShoppingCartRepository>().As<IShoppingCartRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<OrderRepository>().As<IOrderRepository>().InstancePerLifetimeScope();
    containerBuilder.RegisterType<ReferenceDataRepository>().As<IReferenceDataRepository>().InstancePerLifetimeScope();

    // Generic open-type registration
    containerBuilder.RegisterGeneric(typeof(PaginatedList<>)).As(typeof(IPaginatedList<>)).InstancePerLifetimeScope();

    // File service — AWS S3 or local filesystem
    if (BookstoreConfiguration.GetSetting("Services/FileService") == "aws")
    {
        containerBuilder.RegisterType<AmazonS3Client>().As<IAmazonS3>().SingleInstance();
        containerBuilder.RegisterType<S3FileService>().As<IFileService>().InstancePerLifetimeScope();
    }
    else
    {
        var webRootPath = Path.Combine(builder.Environment.WebRootPath, "Content");
        containerBuilder.RegisterInstance(new LocalFileService(webRootPath)).As<IFileService>();
    }

    // Image validation — AWS Rekognition or local stub
    if (BookstoreConfiguration.GetSetting("Services/ImageValidationService") == "aws")
    {
        containerBuilder.RegisterType<AmazonRekognitionClient>().As<IAmazonRekognition>().SingleInstance();
        containerBuilder.RegisterType<RekognitionImageValidationService>().As<IImageValidationService>().InstancePerLifetimeScope();
    }
    else
    {
        containerBuilder.RegisterType<LocalImageValidationService>().As<IImageValidationService>().InstancePerLifetimeScope();
    }
});

// ── MVC + global filters (was FilterConfig.RegisterGlobalFilters) ─────────
builder.Services.AddControllersWithViews(options =>
{
    // Global [Authorize] — equivalent to filters.Add(new AuthorizeAttribute())
    // Handled via FallbackPolicy below so attribute-based [AllowAnonymous] still works.
});

builder.Services.AddHttpContextAccessor();

// ── Authentication (was AuthenticationSetup) ──────────────────────────────
var authMode = builder.Configuration["Services/Authentication"];

if (authMode == "aws")
{
    // AWS Cognito: Cookie + OpenID Connect
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.MetadataAddress = builder.Configuration["Authentication/Cognito/MetadataAddress"]!;
        options.ClientId = builder.Configuration["Authentication/Cognito/LocalClientId"]!;
        options.SaveTokens = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        // Map Cognito claims so downstream code can use them
        options.ClaimActions.MapUniqueJsonKey("cognito:username", "cognito:username");
        options.ClaimActions.MapUniqueJsonKey("cognito:groups", "cognito:groups");

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            NameClaimType = "cognito:username",
            RoleClaimType = "cognito:groups"
        };

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async context =>
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;
                if (identity != null)
                {
                    // Ensure the nameidentifier claim is present for GetSub() calls
                    var sub = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? identity.FindFirst("sub")?.Value;
                    if (sub != null && identity.FindFirst("nameidentifier") == null)
                    {
                        identity.AddClaim(new Claim("nameidentifier", sub));
                    }

                    // Persist customer details on login
                    var customerService = context.HttpContext.RequestServices.GetRequiredService<ICustomerService>();
                    var dto = new CreateOrUpdateCustomerDto(
                        sub ?? string.Empty,
                        identity.Name ?? string.Empty,
                        identity.FindFirst("given_name")?.Value ?? string.Empty,
                        identity.FindFirst("family_name")?.Value ?? string.Empty);
                    await customerService.CreateOrUpdateCustomerAsync(dto);
                }
            }
        };
    });
}
else
{
    // Local development: cookie auth + LocalAuthenticationMiddleware
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Authentication/Login";
        });
}

// ── Authorization (was FilterConfig global [Authorize]) ───────────────────
builder.Services.AddAuthorization(options =>
{
    // FallbackPolicy requires authenticated users for all endpoints by default,
    // equivalent to the MVC 5 global AuthorizeAttribute.
    // Controllers/actions marked [AllowAnonymous] are exempted.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ---------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------
var app = builder.Build();

// ── NLog runtime configuration (was LoggingSetup.ConfigureLogging) ────────
ConfigureNLog(app.Configuration);

// ── Exception handling (was Application_Error) ────────────────────────────
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

// Local authentication middleware (must run after UseRouting, before UseAuthentication
// so it can set the principal before the auth middleware evaluates it).
if (authMode != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

app.UseAuthentication();
app.UseAuthorization();

// ── Routing (was RouteConfig.RegisterRoutes + AreaRegistration.RegisterAllAreas) ──
// Admin area route (was AdminAreaRegistration)
app.MapControllerRoute(
    name: "Admin_default",
    pattern: "Admin/{controller}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

// Default route (was RouteConfig default)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// [PORT-NOTE] BundleConfig.RegisterBundles dropped: System.Web.Optimization is
// unsupported on ASP.NET Core. Static assets are served directly from wwwroot/.
// Views should use direct <link>/<script> tags referencing ~/Content/ and ~/Scripts/.

app.Run();

// ---------------------------------------------------------------------------
// Helper: configure NLog targets at runtime (was LoggingSetup.ConfigureLogging)
// ---------------------------------------------------------------------------
static void ConfigureNLog(IConfiguration configuration)
{
    var config = new LoggingConfiguration();

    Target loggingTarget;

    if (configuration["Services/LoggingService"] == "aws")
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


