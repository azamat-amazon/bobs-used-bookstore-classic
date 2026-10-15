using System.IO;
using System.Reflection;
using System.Security.Claims;
using Amazon.Rekognition;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
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
using Bookstore.Web.Areas;
using Bookstore.Web.Helpers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration: load appsettings.json (automatic), then initialize the
// legacy BookstoreConfiguration singleton so existing callers continue to
// work. BookstoreConfiguration.Initialize reads from "AppSettings" and
// "ConnectionStrings" sections. Because the appsettings.json now uses
// hierarchical JSON keys (e.g. "Services:Authentication"), we populate
// BookstoreConfiguration manually below to bridge the slash-keyed API
// that callers such as AuthenticationController.cs still use.
// ---------------------------------------------------------------------------
var configuration = builder.Configuration;

// Populate BookstoreConfiguration with the keys that callers expect via
// GetSetting("Services/Authentication") etc. These map to the hierarchical
// JSON structure in appsettings.json.
BookstoreConfiguration.Initialize(configuration);

// Manually bridge top-level and hierarchical keys into BookstoreConfiguration
// so that existing GetSetting("Services/Authentication") calls work.
BookstoreConfiguration.AddSetting("Environment", configuration["Environment"] ?? "Development");
BookstoreConfiguration.AddSetting("Services/Authentication", configuration["Services:Authentication"] ?? "local");
BookstoreConfiguration.AddSetting("Services/Database", configuration["Services:Database"] ?? "local");
BookstoreConfiguration.AddSetting("Services/FileService", configuration["Services:FileService"] ?? "local");
BookstoreConfiguration.AddSetting("Services/ImageValidationService", configuration["Services:ImageValidationService"] ?? "local");
BookstoreConfiguration.AddSetting("Services/LoggingService", configuration["Services:LoggingService"] ?? "local");
BookstoreConfiguration.AddSetting("Authentication/Cognito/LocalClientId", configuration["Authentication:Cognito:LocalClientId"] ?? string.Empty);
BookstoreConfiguration.AddSetting("Authentication/Cognito/AppRunnerClientId", configuration["Authentication:Cognito:AppRunnerClientId"] ?? string.Empty);
BookstoreConfiguration.AddSetting("Authentication/Cognito/MetadataAddress", configuration["Authentication:Cognito:MetadataAddress"] ?? string.Empty);
BookstoreConfiguration.AddSetting("Authentication/Cognito/CognitoDomain", configuration["Authentication:Cognito:CognitoDomain"] ?? string.Empty);
BookstoreConfiguration.AddSetting("Files/BucketName", configuration["Files:BucketName"] ?? string.Empty);
BookstoreConfiguration.AddSetting("Files/CloudFrontDomain", configuration["Files:CloudFrontDomain"] ?? string.Empty);
BookstoreConfiguration.AddConnectionString("BookstoreDatabaseConnection",
    configuration.GetConnectionString("BookstoreDatabaseConnection") ?? string.Empty);

// Helper to read a service flag consistently
string GetServiceFlag(string key) => configuration[$"Services:{key}"] ?? "local";

// ---------------------------------------------------------------------------
// AWS SSM Parameter Store overrides (replaces ConfigurationSetup.cs)
// When a service flag says "aws", pull settings from SSM and push them
// into the BookstoreConfiguration singleton.
// ---------------------------------------------------------------------------
var ssmRootPath = "/" + Constants.AppName;

if (GetServiceFlag("Database") == "aws")
{
    using var ssmClient = new AmazonSimpleSystemsManagementClient();
    var request = new GetParameterRequest
    {
        Name = $"{ssmRootPath}/Database/ConnectionStrings/BookstoreDatabaseConnection"
    };
    var response = await ssmClient.GetParameterAsync(request);
    BookstoreConfiguration.AddConnectionString(
        "BookstoreDatabaseConnection",
        response.Parameter.Value);
}

if (GetServiceFlag("Authentication") == "aws")
{
    using var ssmClient = new AmazonSimpleSystemsManagementClient();
    var request = new GetParametersByPathRequest
    {
        Path = $"{ssmRootPath}/Authentication/",
        Recursive = true
    };
    var response = await ssmClient.GetParametersByPathAsync(request);
    foreach (var parameter in response.Parameters)
    {
        BookstoreConfiguration.AddSetting(
            parameter.Name.Replace($"{ssmRootPath}/", string.Empty),
            parameter.Value);
    }
}

if (GetServiceFlag("FileService") == "aws")
{
    using var ssmClient = new AmazonSimpleSystemsManagementClient();
    var request = new GetParametersByPathRequest
    {
        Path = $"{ssmRootPath}/Files/",
        Recursive = true
    };
    var response = await ssmClient.GetParametersByPathAsync(request);
    foreach (var parameter in response.Parameters)
    {
        BookstoreConfiguration.AddSetting(
            parameter.Name.Replace($"{ssmRootPath}/", string.Empty),
            parameter.Value);
    }
}

// ---------------------------------------------------------------------------
// NLog logging (replaces LoggingSetup.cs)
// ---------------------------------------------------------------------------
var nlogConfig = new LoggingConfiguration();

NLog.Targets.Target loggingTarget;
if (GetServiceFlag("LoggingService") == "aws")
{
    loggingTarget = new AWSTarget { LogGroup = Constants.AppName };
}
else
{
    loggingTarget = new DebuggerTarget();
}

nlogConfig.AddTarget("aws", loggingTarget);
nlogConfig.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Info, loggingTarget));
LogManager.Configuration = nlogConfig;

// ---------------------------------------------------------------------------
// Services (replaces DependencyInjectionSetup.cs — Autofac → built-in DI)
// ---------------------------------------------------------------------------

builder.Services.AddControllersWithViews();

builder.Services.AddHttpContextAccessor();

// EF6 DbContext — scoped per request, injected with the connection string.
var dbConnectionString = BookstoreConfiguration.GetConnectionString("BookstoreDatabaseConnection");
builder.Services.AddScoped(_ => new ApplicationDbContext(dbConnectionString));

// Domain services
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

// Generic PaginatedList<T>
builder.Services.AddScoped(typeof(IPaginatedList<>), typeof(PaginatedList<>));

// File service — conditional on config
if (GetServiceFlag("FileService") == "aws")
{
    builder.Services.AddSingleton<IAmazonS3, AmazonS3Client>();
    builder.Services.AddScoped<IFileService, S3FileService>();
}
else
{
    var webRootPath = builder.Environment.WebRootPath
        ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    builder.Services.AddSingleton<IFileService>(new LocalFileService(webRootPath));
}

// Image validation service — conditional on config
if (GetServiceFlag("ImageValidationService") == "aws")
{
    builder.Services.AddSingleton<IAmazonRekognition, AmazonRekognitionClient>();
    builder.Services.AddScoped<IImageValidationService, RekognitionImageValidationService>();
}
else
{
    builder.Services.AddScoped<IImageValidationService, LocalImageValidationService>();
}

// ---------------------------------------------------------------------------
// Authentication (replaces AuthenticationSetup.cs)
// ---------------------------------------------------------------------------
if (GetServiceFlag("Authentication") == "aws")
{
    // Cognito OIDC authentication
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddOpenIdConnect(options =>
        {
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.MetadataAddress = BookstoreConfiguration.GetSetting("Authentication/Cognito/MetadataAddress");
            options.ClientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true
            };

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = async context =>
                {
                    // Preserve the side-effect: create or update customer on login
                    var identity = context.Principal?.Identity as ClaimsIdentity;
                    if (identity != null)
                    {
                        var customerService = context.HttpContext.RequestServices
                            .GetRequiredService<ICustomerService>();
                        var dto = new CreateOrUpdateCustomerDto(
                            identity.FindFirst("nameidentifier")?.Value
                                ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
                            identity.Name ?? string.Empty,
                            identity.FindFirst("given_name")?.Value
                                ?? identity.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty,
                            identity.FindFirst("family_name")?.Value
                                ?? identity.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty);
                        await customerService.CreateOrUpdateCustomerAsync(dto);
                    }
                },
                OnRedirectToIdentityProvider = context =>
                {
                    // Allow customization of the redirect if needed
                    return Task.CompletedTask;
                }
            };
        });
}
else
{
    // Local development authentication — cookie-based
    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Authentication/Login";
        });
}

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Build the app
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware pipeline (order is load-bearing — do not rearrange)
// ---------------------------------------------------------------------------

// Application_Error → centralised exception handler (replaces FilterConfig
// HandleErrorAttribute and the Application_Error handler).
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

// [PORT-NOTE] Dropped BundleConfig.RegisterBundles — Web Optimization is not
// supported on ASP.NET Core. Static assets are served directly from wwwroot/
// via UseStaticFiles().
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Local authentication middleware — only when not using Cognito.
// This must run AFTER UseAuthentication/UseAuthorization so the cookie
// pipeline is initialized, but before endpoints so the principal is set.
if (GetServiceFlag("Authentication") != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

// ---------------------------------------------------------------------------
// Endpoint routing (replaces RouteConfig.cs + AdminAreaRegistration)
// ---------------------------------------------------------------------------

// Admin area route (replaces AdminAreaRegistration / AreaRegistration.RegisterAllAreas)
AdminAreaRegistration.MapAdminArea(app);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
