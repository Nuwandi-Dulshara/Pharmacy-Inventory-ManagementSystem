using System.Windows;
using System.Windows.Media;
using MediStock.Data;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop;

public partial class App : Application
{
}

public sealed class DesktopServices(AuthService authService)
{
    public AuthService Auth { get; } = authService;

    public MediStockDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MediStockDbContext>()
            .UseSqlite(Auth.ConnectionString)
            .Options;
        return new MediStockDbContext(options);
    }

    public string GetSetting(string key, string fallback)
    {
        using var db = CreateDbContext();
        return db.AppSettings.Find(key)?.Value ?? fallback;
    }
}

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        var dark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        Set("CanvasBrush", dark ? "#102B20" : "#E5F3EA");
        Set("SurfaceBrush", dark ? "#193A2A" : "#FFFFFF");
        Set("TextBrush", dark ? "#E5EEE8" : "#153D30");
        Set("MutedBrush", dark ? "#A8B9AF" : "#526E61");
        Set("AccentBrush", dark ? "#37A878" : "#08784F");
        Set("AccentLightBrush", dark ? "#204D38" : "#D6EFDF");
        Set("BorderBrush", dark ? "#355B46" : "#BEDCC9");
        Set("DangerBrush", dark ? "#FF7B72" : "#B42318");
        Set("InputBrush", dark ? "#193A2A" : "#F3FAF5");
        Set("AmberLightBrush", dark ? "#514426" : "#FFF0CC");
    }

    private static void Set(string key, string color) =>
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
