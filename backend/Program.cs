using MediStock;
using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var currentDirectory = Directory.GetCurrentDirectory();
var frontendWebRoot = Path.Combine(currentDirectory, "..", "frontend", "wwwroot");
if (!Directory.Exists(frontendWebRoot))
{
    frontendWebRoot = Path.Combine(currentDirectory, "frontend", "wwwroot");
}
var frontendContentRoot = Path.GetDirectoryName(Path.GetFullPath(frontendWebRoot))!;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = frontendContentRoot,
    WebRootPath = Directory.Exists(frontendWebRoot) ? Path.GetFullPath(frontendWebRoot) : null
});

builder.Services.AddRazorPages();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddDbContext<MediStockDbContext>((serviceProvider, options) =>
    options.UseSqlite(serviceProvider.GetRequiredService<AuthService>().ConnectionString));
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddSingleton<PdfExportService>();
builder.Services.AddScoped<DemandPredictionService>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = ".MediStock.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.Services.GetRequiredService<AuthService>().Initialize();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
