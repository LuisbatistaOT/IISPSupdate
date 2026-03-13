using System.Security.Claims;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.SqlServer;
using IISPSupdate.Data;
using IISPSupdate.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");
var hangfireConnection = builder.Configuration.GetConnectionString("HangfireConnection") ?? defaultConnection;
var hangfireWorkerCount = Math.Clamp(builder.Configuration.GetValue<int?>("Hangfire:WorkerCount") ?? 15, 1, 50);

// Configure database (EF Core + SQL Server)
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(defaultConnection, sql =>
    {
        // Retry transient SQL outages and reduce user-facing failures.
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null);
        sql.CommandTimeout(60);
    }));

// Configure Hangfire with SQL Server storage
builder.Services.AddHangfire(configuration =>
{
    configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                 .UseSimpleAssemblyNameTypeSerializer()
                 .UseRecommendedSerializerSettings()
                 .UseSqlServerStorage(hangfireConnection, new SqlServerStorageOptions
                 {
                     QueuePollInterval = TimeSpan.FromSeconds(10),
                     SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                     CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                     UseRecommendedIsolationLevel = true
                 });
});

builder.Services.AddHangfireServer(options =>
{
    // Keep concurrency configurable; high values can saturate SQL connection pools.
    options.WorkerCount = hangfireWorkerCount;
    options.Queues = new[] { "scan", "install", "verify", "default" };
});

// Application services
builder.Services.AddScoped<PowerShellUpdateService>();
builder.Services.AddScoped<ProjectService>();

// MVC + API (with string enum serialization for clean JSON)
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Windows Authentication (IIS)
builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme)
    .AddNegotiate(); // For local Kestrel debugging with Windows auth

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

var app = builder.Build();

// Configure HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
var webpageRoot = Path.Combine(app.Environment.ContentRootPath, "Webpage");
var hasWebpage = Directory.Exists(webpageRoot);
if (hasWebpage)
{
    // Serve standalone landing/docs assets from /Webpage without affecting MVC static files.
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(webpageRoot),
        RequestPath = "/Webpage"
    });
}
else
{
    app.Logger.LogWarning("Webpage folder was not found at {WebpageRoot}. Falling back to MVC landing page.", webpageRoot);
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Hangfire dashboard (secured by Windows Auth / role checks if needed)
app.UseHangfireDashboard("/hangfire");

app.MapGet("/assets/logo", (IWebHostEnvironment env) =>
{
    var file = Path.Combine(env.ContentRootPath, "logo.png");
    return System.IO.File.Exists(file)
        ? Results.File(file, "image/png")
        : Results.NotFound();
});

app.MapGet("/assets/favicon", (IWebHostEnvironment env) =>
{
    var pngFile = Path.Combine(env.ContentRootPath, "favicon.png");
    if (System.IO.File.Exists(pngFile))
    {
        return Results.File(pngFile, "image/png");
    }

    var jpegFile = Path.Combine(env.ContentRootPath, "favicon.jpeg");
    return System.IO.File.Exists(jpegFile)
        ? Results.File(jpegFile, "image/jpeg")
        : Results.NotFound();
});

if (hasWebpage)
{
    // Prefer dashboard landing when standalone webpage assets are deployed.
    app.MapGet("/", () => Results.Redirect("/Webpage/index.html"));
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
