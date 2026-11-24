using System.Windows.Controls;
using MediStock;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView(DesktopServices services, AuthenticatedUser user)
    {
        InitializeComponent();
        WelcomeText.Text = $"Welcome, {user.FullName} — {user.PharmacyName}";
        using var db = services.CreateDbContext();
        var now = DateTime.UtcNow;
        var lowStock = ParseSetting(services.GetSetting("LowStockQuantity", "10"), 10);
        var expiryDays = ParseSetting(services.GetSetting("ExpiryWarningDays", "30"), 30);
        MedicinesCount.Text = db.Medicines.Count(medicine => medicine.IsActive).ToString();
        StockCount.Text = db.StockBatches.Sum(batch => (int?)batch.Quantity ?? 0).ToString();
        SalesToday.Text = db.Sales.Count(sale => sale.SoldAtUtc >= now.Date && sale.SoldAtUtc < now.Date.AddDays(1)).ToString();

        var batches = db.StockBatches.Include(batch => batch.Medicine).Include(batch => batch.Supplier)
            .Where(batch => batch.Quantity <= lowStock || batch.ExpiryDate <= now.Date.AddDays(expiryDays))
            .OrderBy(batch => batch.ExpiryDate).Take(100).ToList();
        AlertsGrid.ItemsSource = batches.Select(batch => new
        {
            Medicine = batch.Medicine.Name,
            Batch = batch.BatchNumber,
            batch.Quantity,
            Expiry = batch.ExpiryDate,
            Alert = batch.Quantity <= lowStock && batch.ExpiryDate.Date <= now.Date.AddDays(expiryDays)
                ? "Low stock / expiring"
                : batch.Quantity <= lowStock ? "Low stock" : "Expiring"
        }).ToList();
    }

    private static int ParseSetting(string value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
