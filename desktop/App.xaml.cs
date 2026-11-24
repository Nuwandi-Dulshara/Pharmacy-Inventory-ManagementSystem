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
        Set("CanvasBrush", dark ? "#17231D" : "#F4F7F5");
        Set("SurfaceBrush", dark ? "#223129" : "#FFFFFF");
        Set("TextBrush", dark ? "#E5EEE8" : "#18352B");
        Set("MutedBrush", dark ? "#A8B9AF" : "#66766F");
        Set("AccentBrush", dark ? "#37A878" : "#176B4D");
        Set("AccentLightBrush", dark ? "#294638" : "#E4F2EA");
        Set("BorderBrush", dark ? "#3B4D42" : "#DCE5DF");
        Set("DangerBrush", dark ? "#FF7B72" : "#B42318");
    }

    private static void Set(string key, string color) =>
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
