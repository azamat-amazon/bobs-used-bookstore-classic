using Amazon.Rekognition;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
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
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Configuration — initialize BookstoreConfiguration from IConfiguration
//    (replaces ConfigurationSetup.ConfigureConfiguration)
// ---------------------------------------------------------------------------
BookstoreConfiguration.Initialize(builder.Configuration);

// Cache the service mode settings. IConfiguration uses ":" as separator;
// the original web.config appSettings used "/". We read from IConfiguration
// directly here and feed BookstoreConfiguration via AddSetting with the
// "/" key that the rest of the codebase expects.
var authMode = builder.Configuration["Services:Authentication"] ?? "local";
var dbMode = builder.Configuration["Services:Database"] ?? "local";
var fileMode = builder.Configuration["Services:FileService"] ?? "local";
var imageMode = builder.Configuration["Services:ImageValidationService"] ?? "local";
var logMode = builder.Configuration["Services:LoggingService"] ?? "local";

// Ensure the legacy "/" keys are available in BookstoreConfiguration for
// consumers throughout the codebase that call GetSetting("Services/…").
BookstoreConfiguration.AddSetting("Services/Authentication", authMode);
BookstoreConfiguration.AddSetting("Services/Database", dbMode);
BookstoreConfiguration.AddSetting("Services/FileService", fileMode);
BookstoreConfiguration.AddSetting("Services/ImageValidationService", imageMode);
BookstoreConfiguration.AddSetting("Services/LoggingService", logMode);

// Load SSM parameters when running in AWS mode, mirroring the original
// ConfigurationSetup behaviour that fetched connection strings and auth
// settings from AWS Systems Manager Parameter Store.
var rootPath = "/" + Constants.AppName;

if (dbMode == "aws")
{
    using var ssmClient = new AmazonSimpleSystemsManagementClient();
    var request = new GetParameterRequest
    {
        Name = $"{rootPath}/Database/ConnectionStrings/BookstoreDatabaseConnection"
    };
    var response = await ssmClient.GetParameterAsync(request);
    BookstoreConfiguration.AddSetting(
        response.Parameter.Name.Replace($"{rootPath}/Database/", string.Empty),
        response.Parameter.Value);
    BookstoreConfiguration.AddConnectionString(
        "BookstoreDatabaseConnection", response.Parameter.Value);
}

if (authMode == "aws")
{
    using var ssmClient = new AmazonSimpleSystemsManagementClient();
    var request = new GetParametersByPathRequest
    {
        Path = $"{rootPath}/Authentication/",
        Recursive = true
    };
    var response = await ssmClient.GetParametersByPathAsync(request);
    foreach (var parameter in response.Parameters)
    {
        BookstoreConfiguration.AddSetting(
            parameter.Name.Replace($"{rootPath}/", string.Empty),
            parameter.Value);
    }
}

if (fileMode == "aws")
{
    using var ssmClient = new AmazonSimpleSystemsManagementClient();
    var request = new GetParametersByPathRequest
    {
        Path = $"{rootPath}/Files/",
        Recursive = true
    };
    var response = await ssmClient.GetParametersByPathAsync(request);
    foreach (var parameter in response.Parameters)
    {
        BookstoreConfiguration.AddSetting(
            parameter.Name.Replace($"{rootPath}/", string.Empty),
            parameter.Value);
    }
}

// Also seed BookstoreConfiguration with Cognito and Files settings from
// appsettings.json (for the "/" key format the rest of the code expects).
var cognitoSection = builder.Configuration.GetSection("Authentication:Cognito");
foreach (var child in cognitoSection.GetChildren())
{
    BookstoreConfiguration.AddSetting($"Authentication/Cognito/{child.Key}", child.Value ?? string.Empty);
}

var filesSection = builder.Configuration.GetSection("Files");
foreach (var child in filesSection.GetChildren())
{
    BookstoreConfiguration.AddSetting($"Files/{child.Key}", child.Value ?? string.Empty);
}

// ---------------------------------------------------------------------------
// 2. Logging — NLog.Web.AspNetCore (replaces LoggingSetup.ConfigureLogging)
// ---------------------------------------------------------------------------
var nlogConfig = new LoggingConfiguration();

NLog.Targets.Target loggingTarget;
if (logMode == "aws")
{
    loggingTarget = new AWSTarget { LogGroup = Constants.AppName };
}
else
{
    loggingTarget = new DebuggerTarget();
}

nlogConfig.AddTarget("logging", loggingTarget);
nlogConfig.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Info, loggingTarget));
LogManager.Configuration = nlogConfig;

builder.Logging.ClearProviders();
builder.Host.UseNLog();

// ---------------------------------------------------------------------------
// 3. MVC + global filters (replaces FilterConfig.RegisterGlobalFilters)
// ---------------------------------------------------------------------------
// The original FilterConfig added HandleErrorAttribute (handled by
// UseExceptionHandler middleware below) and a global AuthorizeAttribute
// (replaced by a fallback authorization policy requiring authenticated users).
builder.Services.AddControllersWithViews();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ---------------------------------------------------------------------------
// 4. Authentication (replaces AuthenticationSetup.ConfigureAuthentication)
//    Supports both local cookie-based auth and AWS Cognito OpenID Connect.
// ---------------------------------------------------------------------------
if (authMode == "aws")
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.MetadataAddress = BookstoreConfiguration.GetSetting("Authentication/Cognito/MetadataAddress");
        options.ClientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
        options.SaveTokens = true;
        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProviderForSignOut = context =>
            {
                var domain = BookstoreConfiguration.GetSetting("Authentication/Cognito/CognitoDomain");
                var clientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
                var logoutUri = $"{context.Request.Scheme}://{context.Request.Host}/";
                context.Response.Redirect($"{domain}/logout?client_id={clientId}&logout_uri={logoutUri}");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    });
}
else
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Authentication/Login";
        });
}

// ---------------------------------------------------------------------------
// 5. EF Core DbContext
// ---------------------------------------------------------------------------
var connectionString = BookstoreConfiguration.GetConnectionString("BookstoreDatabaseConnection")
    ?? builder.Configuration.GetConnectionString("BookstoreDatabaseConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ---------------------------------------------------------------------------
// 6. Autofac DI container (replaces DependencyInjectionSetup)
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

    // Generic
    containerBuilder.RegisterGeneric(typeof(PaginatedList<>)).As(typeof(IPaginatedList<>)).InstancePerLifetimeScope();

    // File service — S3 in AWS, local filesystem otherwise
    if (fileMode == "aws")
    {
        containerBuilder.RegisterType<AmazonS3Client>().As<IAmazonS3>().SingleInstance();
        containerBuilder.RegisterType<S3FileService>().As<IFileService>().InstancePerLifetimeScope();
    }
    else
    {
        containerBuilder.Register(ctx =>
        {
            var env = ctx.Resolve<IWebHostEnvironment>();
            var webRootPath = Path.Combine(env.WebRootPath, "Content");
            return new LocalFileService(webRootPath);
        }).As<IFileService>().InstancePerLifetimeScope();
    }

    // Image validation service — Rekognition in AWS, local stub otherwise
    if (imageMode == "aws")
    {
        containerBuilder.RegisterType<AmazonRekognitionClient>().As<IAmazonRekognition>().SingleInstance();
        containerBuilder.RegisterType<RekognitionImageValidationService>().As<IImageValidationService>().InstancePerLifetimeScope();
    }
    else
    {
        containerBuilder.RegisterType<LocalImageValidationService>().As<IImageValidationService>().InstancePerLifetimeScope();
    }
});

builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// Build the app
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// 7. Middleware pipeline
// ---------------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    // Application_Error → centralised exception handler
    app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (ex is not null)
        {
            var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Unhandled exception");
        }
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    }));
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
app.UseAuthorization();

// Local authentication middleware — registered in the pipeline only when not
// using Cognito. This replaces the original Autofac-resolved middleware.
if (authMode != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

// ---------------------------------------------------------------------------
// 8. Endpoint mapping (replaces RouteConfig + AdminAreaRegistration)
// ---------------------------------------------------------------------------

// Admin area route (replaces AdminAreaRegistration.RegisterArea)
app.MapControllerRoute(
    name: "Admin_default",
    pattern: "Admin/{controller=Dashboard}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

// Default route (replaces RouteConfig.RegisterRoutes)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// [PORT-NOTE] dropped BundleConfig.RegisterBundles: System.Web.Optimization
// bundling is not supported on ASP.NET Core. Static assets are served directly
// from wwwroot/ (Content/ and Scripts/ subdirectories). Views reference
// scripts and stylesheets via direct <link>/<script> tags.

app.Run();
