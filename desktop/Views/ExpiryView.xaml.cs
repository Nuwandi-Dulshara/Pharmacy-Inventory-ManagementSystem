using System.Windows;
using System.Windows.Controls;
using MediStock.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop.Views;

public partial class ExpiryView : UserControl
{
    private readonly DesktopServices _services;
    private readonly bool _canDelete;
    private int _warningDays;
    private List<ExpiryRow> _rows = new();

    public ExpiryView(DesktopServices services, bool canDelete)
    {
        InitializeComponent();
        _services = services;
        _canDelete = canDelete;
        DeleteButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        _warningDays = ReadExpiryWarningDays();
        WarningText.Text = $"Expiry warnings use the shared {_warningDays}-day setting. Only unused batches with zero stock can be deleted.";
        LoadRows();
    }

    private int ReadExpiryWarningDays()
    {
        var configured = _services.GetSetting("ExpiryWarningDays", "30");
        return int.TryParse(configured, out var days) && days > 0 ? days : 30;
    }

    private void LoadRows()
    {
        try
        {
            using var db = _services.CreateDbContext();
            var today = DateTime.UtcNow.Date;
            var cutoff = today.AddDays(_warningDays);
            _rows = db.StockBatches.AsNoTracking()
                .Include(batch => batch.Medicine)
                .OrderBy(batch => batch.ExpiryDate)
                .ThenBy(batch => batch.Medicine.Name)
                .Select(batch => new ExpiryRow
                {
                    Id = batch.Id,
                    Medicine = batch.Medicine.Name,
                    BatchNumber = batch.BatchNumber,
                    Quantity = batch.Quantity,
                    ExpiryDate = batch.ExpiryDate,
                    Status = batch.ExpiryDate.Date < today
                        ? "Expired"
                        : batch.ExpiryDate.Date <= cutoff ? "Expiring soon" : "Usable"
                }).ToList();
            ApplyFilters();
        }
        catch (Exception exception) when (
            exception is SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not load expiry data: {exception.Message}";
        }
    }

    private void ApplyFilters()
    {
        var query = _rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            var search = SearchBox.Text.Trim();
            query = query.Where(row =>
                row.Medicine.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.BatchNumber.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var filter = (FilterBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (filter is "Expired" or "Expiring soon" or "Usable")
            query = query.Where(row => row.Status == filter);
        BatchesGrid.ItemsSource = query.ToList();
        StatusText.Text = $"{BatchesGrid.Items.Count} batch(es) match.";
    }

    private void ApplyFilters_Click(object sender, RoutedEventArgs e) => ApplyFilters();

    private void BatchesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BatchesGrid.SelectedItem is ExpiryRow row)
            StatusText.Text = row.Quantity == 0
                ? $"{row.Medicine} / {row.BatchNumber}: zero quantity; deletion eligibility is checked when requested."
                : $"{row.Medicine} / {row.BatchNumber}: batches with stock cannot be deleted.";
    }

    private void DeleteBatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_canDelete || BatchesGrid.SelectedItem is not ExpiryRow selected)
        {
            StatusText.Text = "Select a batch to delete.";
            return;
        }

        if (MessageBox.Show(
                $"Delete empty batch {selected.BatchNumber} of {selected.Medicine}? This cannot be undone.",
                "Confirm batch deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var db = _services.CreateDbContext();
            using var transaction = db.Database.BeginTransaction();
            var batch = db.StockBatches.SingleOrDefault(item => item.Id == selected.Id);
            if (batch is null)
            {
                StatusText.Text = "The batch no longer exists.";
                return;
            }
            if (batch.Quantity != 0 ||
                db.SaleItems.Any(item => item.StockBatchId == batch.Id) ||
                db.StockAdjustments.Any(item => item.StockBatchId == batch.Id))
            {
                StatusText.Text = "Only unused batches with zero quantity and no sale or adjustment history may be deleted.";
                return;
            }

            db.StockBatches.Remove(batch);
            db.SaveChanges();
            transaction.Commit();
            LoadRows();
            StatusText.Text = "Empty batch deleted.";
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException or OverflowException)
        {
            StatusText.Text = $"Could not delete the batch: {exception.Message}";
        }
    }

    private sealed class ExpiryRow
    {
        public int Id { get; init; }
        public string Medicine { get; init; } = string.Empty;
        public string BatchNumber { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public DateTime ExpiryDate { get; init; }
        public string Status { get; init; } = string.Empty;
    }
}
