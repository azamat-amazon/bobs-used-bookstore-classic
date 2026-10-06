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
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Configuration — initialize BookstoreConfiguration from IConfiguration
//    (replaces ConfigurationSetup.ConfigureConfiguration)
// ---------------------------------------------------------------------------
BookstoreConfiguration.Initialize(builder.Configuration);

// Load additional settings from AWS SSM Parameter Store when configured for AWS
var rootPath = "/" + Constants.AppName;

if (BookstoreConfiguration.GetSetting("Services/Database") == "aws")
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
}

if (BookstoreConfiguration.GetSetting("Services/Authentication") == "aws")
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

if (BookstoreConfiguration.GetSetting("Services/FileService") == "aws")
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

// ---------------------------------------------------------------------------
// 2. Logging — NLog configuration (replaces LoggingSetup.ConfigureLogging)
// ---------------------------------------------------------------------------
var nlogConfig = new LoggingConfiguration();
NLog.Targets.Target loggingTarget;

if (BookstoreConfiguration.GetSetting("Services/LoggingService") == "aws")
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
// 3. Services — MVC + Autofac DI (replaces DependencyInjectionSetup)
// ---------------------------------------------------------------------------

// Use Autofac as the DI container (Autofac.Extensions.DependencyInjection already in csproj)
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// FilterConfig: global AuthorizeAttribute → fallback authorization policy.
// Controllers marked [AllowAnonymous] override this policy.
builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
});

builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// 4. Authentication (replaces AuthenticationSetup)
// ---------------------------------------------------------------------------
if (BookstoreConfiguration.GetSetting("Services/Authentication") == "aws")
{
    // Cognito OIDC authentication
    var cognitoClientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
    var metadataAddress = BookstoreConfiguration.GetSetting("Authentication/Cognito/MetadataAddress");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.ClientId = cognitoClientId;
        options.MetadataAddress = metadataAddress;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                // Preserve any custom redirect logic from the legacy callback
                return Task.CompletedTask;
            },
            OnAuthorizationCodeReceived = context =>
            {
                // Preserve any custom authorization code handling
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                // Replaces the legacy SecurityTokenValidated callback:
                // Map cognito:username and cognito:groups claims, upsert customer
                var identity = context.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
                if (identity != null)
                {
                    var sub = identity.FindFirst("sub")?.Value
                              ?? identity.FindFirst("nameidentifier")?.Value;
                    var givenName = identity.FindFirst("given_name")?.Value ?? string.Empty;
                    var familyName = identity.FindFirst("family_name")?.Value ?? string.Empty;
                    var name = identity.FindFirst("cognito:username")?.Value
                               ?? identity.Name ?? string.Empty;

                    // Map cognito:groups to role claims
                    var groupsClaims = identity.FindAll("cognito:groups");
                    foreach (var groupClaim in groupsClaims)
                    {
                        identity.AddClaim(new System.Security.Claims.Claim(
                            System.Security.Claims.ClaimTypes.Role, groupClaim.Value));
                    }

                    // Upsert customer in the database
                    if (!string.IsNullOrEmpty(sub))
                    {
                        var customerService = context.HttpContext.RequestServices
                            .GetRequiredService<ICustomerService>();
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
    // Local cookie-based authentication
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Authentication/Login";
        });
}

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// 5. Autofac container registrations (replaces DependencyInjectionSetup)
// ---------------------------------------------------------------------------
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

    // EF6 DbContext — connection string from configuration (EntityFramework 6 retained, not EF Core)
    var connectionString = BookstoreConfiguration.GetConnectionString("BookstoreDatabaseConnection")
                           ?? builder.Configuration.GetConnectionString("BookstoreDatabaseConnection");
    containerBuilder.RegisterType<ApplicationDbContext>()
        .WithParameter("connectionString", connectionString)
        .InstancePerLifetimeScope();

    // Repositories
    containerBuilder.RegisterType<CustomerRepository>().As<ICustomerRepository>();
    containerBuilder.RegisterType<AddressRepository>().As<IAddressRepository>();
    containerBuilder.RegisterType<BookRepository>().As<IBookRepository>();
    containerBuilder.RegisterType<OfferRepository>().As<IOfferRepository>();
    containerBuilder.RegisterType<ShoppingCartRepository>().As<IShoppingCartRepository>();
    containerBuilder.RegisterType<OrderRepository>().As<IOrderRepository>();
    containerBuilder.RegisterType<ReferenceDataRepository>().As<IReferenceDataRepository>();
    containerBuilder.RegisterGeneric(typeof(PaginatedList<>)).As(typeof(IPaginatedList<>)).InstancePerLifetimeScope();

    // File service — conditional on configuration
    if (BookstoreConfiguration.GetSetting("Services/FileService") == "aws")
    {
        containerBuilder.RegisterType<AmazonS3Client>().As<IAmazonS3>();
        containerBuilder.RegisterType<S3FileService>().As<IFileService>();
    }
    else
    {
        // HttpRuntime.AppDomainAppPath → IWebHostEnvironment.WebRootPath
        var webRootPath = Path.Combine(builder.Environment.WebRootPath ?? builder.Environment.ContentRootPath, "Content");
        containerBuilder.RegisterInstance(new LocalFileService(webRootPath)).As<IFileService>();
    }

    // Image validation service — conditional on configuration
    if (BookstoreConfiguration.GetSetting("Services/ImageValidationService") == "aws")
    {
        containerBuilder.RegisterType<AmazonRekognitionClient>().As<IAmazonRekognition>();
        containerBuilder.RegisterType<RekognitionImageValidationService>().As<IImageValidationService>();
    }
    else
    {
        containerBuilder.RegisterType<LocalImageValidationService>().As<IImageValidationService>();
    }

    // Local authentication middleware (registered only for non-AWS auth)
    if (BookstoreConfiguration.GetSetting("Services/Authentication") != "aws")
    {
        containerBuilder.RegisterType<LocalAuthenticationMiddleware>();
    }
});

// ---------------------------------------------------------------------------
// Build the app
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// 6. Middleware pipeline
// ---------------------------------------------------------------------------

// Application_Error → centralised exception handler
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Admin/Error");
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

// Local authentication middleware (replaces the OWIN LocalAuthenticationMiddleware pipeline)
if (BookstoreConfiguration.GetSetting("Services/Authentication") != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

// ---------------------------------------------------------------------------
// 7. Endpoint mapping (replaces RouteConfig + AdminAreaRegistration)
// ---------------------------------------------------------------------------

// AdminAreaRegistration → MapAreaControllerRoute
app.MapAreaControllerRoute(
    name: "Admin_default",
    areaName: "Admin",
    pattern: "Admin/{controller=Dashboard}/{action=Index}/{id?}");

// RouteConfig.RegisterRoutes → default controller route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// [PORT-NOTE] BundleConfig dropped: System.Web.Optimization bundles (ScriptBundle/StyleBundle)
// are not supported on ASP.NET Core. Static assets are served directly from wwwroot/.
// Views should use direct <script> and <link> tags instead of @Scripts.Render / @Styles.Render.

app.Run();
