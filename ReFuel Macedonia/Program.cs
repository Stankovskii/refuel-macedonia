using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using ReFuel_Macedonia.Data;
using ReFuel_Macedonia.Infrastructure;
using ReFuel_Macedonia.Models;
using ReFuel_Macedonia.Services;
using ReFuel_Macedonia.Services.Ai;
using ReFuel_Macedonia.Services.Ingestion;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ReFuelOptions>(builder.Configuration.GetSection(ReFuelOptions.SectionName));
builder.Services.Configure<CompanyOptions>(builder.Configuration.GetSection(CompanyOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Data Source=refuel.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(connectionString);
    if (builder.Environment.IsDevelopment())
        options.EnableDetailedErrors();
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.Cookie.Name = "ReFuel.Auth";
});

builder.Services.AddSingleton<ITranslator, JsonTranslator>();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = SupportedCultures.All.Select(c => new CultureInfo(c)).ToList();

    options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, new CultureInfo(SupportedCultures.Macedonian));
    options.SupportedCultures = new List<CultureInfo> { CultureInfo.InvariantCulture };
    options.SupportedUICultures = cultures;
    options.ApplyCurrentCultureToResponseHeaders = true;

    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<IProfitabilityService, ProfitabilityService>();
builder.Services.AddScoped<IPriceService, PriceService>();
builder.Services.AddScoped<IStationService, StationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IPromotionService, PromotionService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITrackingService, TrackingService>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<IFleetService, FleetService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();

builder.Services.AddScoped<IPricePredictionService, PricePredictionService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IRouteOptimizationService, RouteOptimizationService>();

builder.Services.AddHttpClient<IOsmImportService, OsmImportService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ReFuelMacedonia/1.0 (diploma project)");
});

builder.Services.AddHttpClient<IErcPriceScraper, ErcPriceScraper>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ReFuelMacedonia/1.0 (diploma project)");
});

builder.Services.AddHttpClient<IWeatherService, WeatherService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ReFuelMacedonia/1.0");
});

builder.Services.AddHttpClient<IRoadConditionService, RoadConditionService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ReFuelMacedonia/1.0");
});

builder.Services.AddHostedService<DataRefreshBackgroundService>();
builder.Services.AddHostedService<TelemetrySimulatorService>();

builder.Services.AddControllersWithViews();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddResponseCompression();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        await DbSeeder.SeedAsync(scope.ServiceProvider, logger);
        logger.LogInformation("ReFuel database is ready.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Seeding the database failed.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();

app.UseRequestLocalization();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
