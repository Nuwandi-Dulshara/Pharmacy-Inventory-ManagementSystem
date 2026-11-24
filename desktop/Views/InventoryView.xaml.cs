using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MediStock.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop.Views;

public partial class InventoryView : UserControl
{
    private readonly DesktopServices _services;
    private readonly bool _canEdit;
    private bool _changingSelection;

    public InventoryView(DesktopServices services, bool canEdit)
    {
        InitializeComponent();
        _services = services;
        _canEdit = canEdit;
        EditorPanel.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        AdjustmentPanel.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        PermissionText.Text = canEdit
            ? "View stock, receive new batches, and record audited quantity adjustments."
            : "Inventory is available in read-only mode for your role.";
        LoadChoices();
        RefreshInventory();
    }

    private void LoadChoices()
    {
        using var db = _services.CreateDbContext();
        var choices = db.Medicines.Where(medicine => medicine.IsActive)
            .OrderBy(medicine => medicine.Name)
            .Select(medicine => new MedicineChoice(medicine.Id, medicine.Name, medicine.GenericName,
                medicine.Brand, medicine.SellingPrice))
            .ToList();
        choices.Insert(0, new MedicineChoice(0, "+ New medicine", null, null, 0));
        _changingSelection = true;
        MedicineBox.ItemsSource = choices;
        MedicineBox.SelectedIndex = 0;
        SupplierBox.ItemsSource = db.Suppliers.Where(supplier => supplier.IsActive)
            .OrderBy(supplier => supplier.Name).ToList();
        SupplierBox.SelectedIndex = SupplierBox.Items.Count > 0 ? 0 : -1;
        _changingSelection = false;
        ExpiryPicker.SelectedDate = DateTime.Today.AddYears(1);
    }

    private void RefreshInventory()
    {
        using var db = _services.CreateDbContext();
        InventoryGrid.ItemsSource = db.StockBatches
            .Include(batch => batch.Medicine)
            .Include(batch => batch.Supplier)
            .OrderBy(batch => batch.ExpiryDate)
            .Select(batch => new InventoryRow
            {
                BatchId = batch.Id,
                Medicine = batch.Medicine.Name,
                Batch = batch.BatchNumber,
                Supplier = batch.Supplier.Name,
                Quantity = batch.Quantity,
                SellingPrice = batch.Medicine.SellingPrice,
                Expiry = batch.ExpiryDate
            }).ToList();
    }

    private void MedicineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || MedicineBox.SelectedItem is not MedicineChoice choice) return;
        NameBox.Text = choice.Id == 0 ? string.Empty : choice.Name;
        GenericBox.Text = choice.Id == 0 ? string.Empty : choice.GenericName ?? string.Empty;
        BrandBox.Text = choice.Id == 0 ? string.Empty : choice.Brand ?? string.Empty;
        SellingPriceBox.Text = choice.Id == 0 ? string.Empty : choice.SellingPrice.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private void SaveStock_Click(object sender, RoutedEventArgs e)
    {
        MessageText.Text = string.Empty;
        if (!_canEdit) return;
        if (string.IsNullOrWhiteSpace(NameBox.Text) ||
            !decimal.TryParse(SellingPriceBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var sellingPrice) || sellingPrice < 0 ||
            !int.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0 ||
            !decimal.TryParse(PurchasePriceBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var purchasePrice) || purchasePrice < 0 ||
            string.IsNullOrWhiteSpace(BatchNumberBox.Text) || ExpiryPicker.SelectedDate is not DateTime expiry ||
            SupplierBox.SelectedItem is not Supplier supplier)
        {
            MessageText.Text = "Enter a medicine, valid prices and quantity, supplier, batch number, and expiry.";
            return;
        }

        try
        {
            using var db = _services.CreateDbContext();
            using var transaction = db.Database.BeginTransaction();
            var selected = MedicineBox.SelectedItem as MedicineChoice;
            Medicine medicine;
            if (selected is { Id: > 0 })
            {
                medicine = db.Medicines.Single(m => m.Id == selected.Id);
                medicine.Name = NameBox.Text.Trim();
                medicine.GenericName = NullIfEmpty(GenericBox.Text);
                medicine.Brand = NullIfEmpty(BrandBox.Text);
                medicine.SellingPrice = sellingPrice;
            }
            else
            {
                medicine = new Medicine
                {
                    Name = NameBox.Text.Trim(),
                    GenericName = NullIfEmpty(GenericBox.Text),
                    Brand = NullIfEmpty(BrandBox.Text),
                    SellingPrice = sellingPrice,
                    IsActive = true,
                    CreatedUtc = DateTime.UtcNow
                };
                db.Medicines.Add(medicine);
                db.SaveChanges();
            }

            if (db.StockBatches.Any(batch => batch.MedicineId == medicine.Id &&
                                             batch.BatchNumber == BatchNumberBox.Text.Trim()))
            {
                MessageText.Text = "That batch number already exists for this medicine.";
                return;
            }

            db.StockBatches.Add(new StockBatch
            {
                MedicineId = medicine.Id,
                SupplierId = supplier.Id,
                BatchNumber = BatchNumberBox.Text.Trim(),
                Quantity = quantity,
                PurchasePrice = purchasePrice,
                ExpiryDate = expiry.Date,
                CreatedUtc = DateTime.UtcNow
            });
            db.SaveChanges();
            transaction.Commit();
            MessageText.Text = "Stock saved.";
            BatchNumberBox.Clear();
            QuantityBox.Clear();
            PurchasePriceBox.Clear();
            RefreshInventory();
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException or OverflowException)
        {
            MessageText.Text = $"Could not save stock: {exception.Message}";
        }
    }

    private void AddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SupplierDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var db = _services.CreateDbContext();
            db.Suppliers.Add(new Supplier
            {
                Name = dialog.SupplierName.Trim(),
                Phone = dialog.Phone.Trim(),
                Email = NullIfEmpty(dialog.Email),
                Address = NullIfEmpty(dialog.Address),
                IsActive = true,
                CreatedUtc = DateTime.UtcNow
            });
            db.SaveChanges();
            LoadChoices();
            MessageText.Text = "Supplier added.";
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException)
        {
            MessageText.Text = $"Could not add supplier: {exception.Message}";
        }
    }

    private void ApplyAdjustment_Click(object sender, RoutedEventArgs e)
    {
        AdjustmentMessage.Text = string.Empty;
        if (!_canEdit || InventoryGrid.SelectedItem is not InventoryRow row ||
            !int.TryParse(AdjustmentBox.Text, out var change) || change == 0 ||
            string.IsNullOrWhiteSpace(AdjustmentReasonBox.Text))
        {
            AdjustmentMessage.Text = "Select a batch, enter a non-zero quantity change and reason.";
            return;
        }

        try
        {
            using var db = _services.CreateDbContext();
            using var transaction = db.Database.BeginTransaction();
            var batch = db.StockBatches.Single(item => item.Id == row.BatchId);
            if (batch.Quantity + change < 0)
            {
                AdjustmentMessage.Text = "Adjustment cannot reduce stock below zero.";
                return;
            }
            batch.Quantity += change;
            db.StockAdjustments.Add(new StockAdjustment
            {
                StockBatchId = batch.Id,
                QuantityChange = change,
                Reason = AdjustmentReasonBox.Text.Trim(),
                AdjustedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
            transaction.Commit();
            AdjustmentBox.Clear();
            AdjustmentReasonBox.Clear();
            AdjustmentMessage.Text = "Adjustment recorded.";
            RefreshInventory();
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException or OverflowException)
        {
            AdjustmentMessage.Text = $"Could not adjust stock: {exception.Message}";
        }
    }

    private void InventoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InventoryGrid.SelectedItem is InventoryRow row)
            AdjustmentMessage.Text = $"Selected {row.Medicine} / {row.Batch}.";
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record MedicineChoice(int Id, string Name, string? GenericName, string? Brand, decimal SellingPrice)
    {
        public string Display => Id == 0 ? Name : $"{Name} ({SellingPrice:C})";
    }

    private sealed class InventoryRow
    {
        public int BatchId { get; init; }
        public string Medicine { get; init; } = string.Empty;
        public string Batch { get; init; } = string.Empty;
        public string Supplier { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal SellingPrice { get; init; }
        public DateTime Expiry { get; init; }
    }
}
