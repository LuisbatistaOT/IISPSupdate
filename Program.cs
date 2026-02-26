using System.Security.Claims;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.SqlServer;
using IISPSupdate.Data;
using IISPSupdate.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure database (EF Core + SQL Server)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Hangfire with SQL Server storage
builder.Services.AddHangfire(configuration =>
{
    configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                 .UseSimpleAssemblyNameTypeSerializer()
                 .UseRecommendedSerializerSettings()
                 .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"), new SqlServerStorageOptions
                 {
                     QueuePollInterval = TimeSpan.FromSeconds(5),
                     SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                     CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                     UseRecommendedIsolationLevel = true
                 });
});

builder.Services.AddHangfireServer(options =>
{
    // Cap total concurrency; queues implement logical separation of work
    options.WorkerCount = 50;
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

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Hangfire dashboard (secured by Windows Auth / role checks if needed)
app.UseHangfireDashboard("/hangfire");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
