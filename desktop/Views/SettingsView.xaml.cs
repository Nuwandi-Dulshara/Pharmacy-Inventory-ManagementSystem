using System.Windows;
using System.Windows.Controls;
using MediStock.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop.Views;

public partial class SettingsView : UserControl
{
    private readonly DesktopServices _services;

    public SettingsView(DesktopServices services)
    {
        InitializeComponent();
        _services = services;
        using var db = services.CreateDbContext();
        LowStockBox.Text = db.AppSettings.Find("LowStockQuantity")?.Value ?? "10";
        ExpiryDaysBox.Text = db.AppSettings.Find("ExpiryWarningDays")?.Value ?? "30";
        var theme = db.AppSettings.Find("Theme")?.Value ?? "Light";
        ThemeBox.SelectedIndex = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        StatusText.Text = string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LowStockBox.Text, out var lowStock) || lowStock < 0 || lowStock > 100_000 ||
            !int.TryParse(ExpiryDaysBox.Text, out var expiryDays) || expiryDays < 1 || expiryDays > 365)
        {
            StatusText.Text = "Low-stock quantity must be 0–100000; expiry days must be 1–365.";
            return;
        }

        try
        {
            var theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Light";
            using var db = _services.CreateDbContext();
            SaveSetting(db, "LowStockQuantity", lowStock.ToString());
            SaveSetting(db, "ExpiryWarningDays", expiryDays.ToString());
            SaveSetting(db, "Theme", theme);
            db.SaveChanges();
            ThemeManager.Apply(theme);
            StatusText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"];
            StatusText.Text = "Settings saved.";
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not save settings: {exception.Message}";
        }
    }

    private static void SaveSetting(MediStock.Data.MediStockDbContext db, string key, string value)
    {
        var setting = db.AppSettings.Find(key);
        if (setting is null)
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else
            setting.Value = value;
    }
}
