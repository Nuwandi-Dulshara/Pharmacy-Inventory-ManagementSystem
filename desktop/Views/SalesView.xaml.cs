using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MediStock;
using MediStock.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop.Views;

public partial class SalesView : UserControl
{
    private readonly DesktopServices _services;
    private readonly AuthenticatedUser _user;
    private readonly string _email;
    private readonly ObservableCollection<CartLine> _cart = new();
    private List<MedicineOption> _medicineOptions = new();

    public SalesView(DesktopServices services, AuthenticatedUser user, string email)
    {
        InitializeComponent();
        _services = services;
        _user = user;
        _email = email;
        CartGrid.ItemsSource = _cart;
        _cart.CollectionChanged += (_, _) => UpdateTotal();
        LoadMedicines();
    }

    private void LoadMedicines()
    {
        using var db = _services.CreateDbContext();
        var today = DateTime.Today;
        var availableByMedicine = db.StockBatches
            .Where(batch => batch.Quantity > 0 && batch.ExpiryDate >= today)
            .GroupBy(batch => batch.MedicineId)
            .Select(group => new { MedicineId = group.Key, Quantity = group.Sum(batch => batch.Quantity) })
            .ToDictionary(item => item.MedicineId, item => item.Quantity);
        _medicineOptions = db.Medicines.Where(medicine => medicine.IsActive)
            .OrderBy(medicine => medicine.Name)
            .ToList()
            .Where(medicine => availableByMedicine.ContainsKey(medicine.Id))
            .Select(medicine => new MedicineOption(medicine.Id, medicine.Name, medicine.SellingPrice,
                availableByMedicine[medicine.Id]))
            .ToList();
        MedicineBox.ItemsSource = _medicineOptions;
        MedicineBox.SelectedIndex = _medicineOptions.Count > 0 ? 0 : -1;
    }

    private void AddToCart_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;
        if (MedicineBox.SelectedItem is not MedicineOption medicine ||
            !int.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0)
        {
            StatusText.Text = "Choose an in-stock medicine and enter a positive quantity.";
            return;
        }

        var line = _cart.SingleOrDefault(item => item.MedicineId == medicine.Id);
        var newQuantity = quantity + (line?.Quantity ?? 0);
        if (newQuantity > medicine.Available)
        {
            StatusText.Text = $"Only {medicine.Available} unexpired units are available.";
            return;
        }
        if (line is null)
            _cart.Add(new CartLine(medicine.Id, medicine.Name, medicine.Available, quantity, medicine.UnitPrice));
        else
            line.Quantity = newQuantity;
        UpdateTotal();
    }

    private void SetQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (CartGrid.SelectedItem is not CartLine line || !int.TryParse(SetQuantityBox.Text, out var quantity) || quantity <= 0)
        {
            StatusText.Text = "Select a cart item and enter a positive quantity.";
            return;
        }
        if (quantity > line.Available)
        {
            StatusText.Text = $"Only {line.Available} unexpired units are available.";
            return;
        }
        line.Quantity = quantity;
        UpdateTotal();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartGrid.SelectedItem is CartLine line)
            _cart.Remove(line);
    }

    private void CartGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CartGrid.SelectedItem is CartLine line)
            SetQuantityBox.Text = line.Quantity.ToString();
    }

    private void CompleteSale_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;
        if (_cart.Count == 0)
        {
            StatusText.Text = "Add at least one medicine to the cart.";
            return;
        }

        var paymentMethod = (PaymentBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
        try
        {
            string invoiceNumber;
            using (var db = _services.CreateDbContext())
            {
                db.Database.OpenConnection();
                var connection = (SqliteConnection)db.Database.GetDbConnection();
                using var transaction = connection.BeginTransaction(deferred: false);
                db.Database.UseTransaction(transaction);

                var sale = new Sale
                {
                    InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}",
                    SoldAtUtc = DateTime.UtcNow,
                    PaymentMethod = paymentMethod,
                    Status = "Completed"
                };
                var total = 0m;
                foreach (var line in _cart)
                {
                    var remaining = line.Quantity;
                    var batches = db.StockBatches
                        .Where(batch => batch.MedicineId == line.MedicineId &&
                                        batch.Quantity > 0 && batch.ExpiryDate >= DateTime.Today)
                        .OrderBy(batch => batch.ExpiryDate)
                        .ThenBy(batch => batch.Id)
                        .ToList();
                    var available = batches.Sum(batch => batch.Quantity);
                    if (available < line.Quantity)
                        throw new InvalidOperationException($"{line.Name}: only {available} unexpired units remain.");

                    foreach (var batch in batches)
                    {
                        if (remaining == 0) break;
                        var take = Math.Min(batch.Quantity, remaining);
                        batch.Quantity -= take;
                        var lineTotal = line.UnitPrice * take;
                        sale.Items.Add(new SaleItem
                        {
                            MedicineId = line.MedicineId,
                            StockBatchId = batch.Id,
                            Quantity = take,
                            UnitPrice = line.UnitPrice,
                            LineTotal = lineTotal
                        });
                        total += lineTotal;
                        remaining -= take;
                    }
                }

                sale.TotalAmount = total;
                db.Sales.Add(sale);
                db.SaveChanges();
                transaction.Commit();
                invoiceNumber = sale.InvoiceNumber;
            }

            _cart.Clear();
            LoadMedicines();
            UpdateTotal();
            StatusText.Text = $"Sale completed. Invoice {invoiceNumber} was saved.";
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException or OverflowException)
        {
            StatusText.Text = $"Sale was not completed: {exception.Message}";
            LoadMedicines();
        }
    }

    private void UpdateTotal() => TotalText.Text = _cart.Sum(item => item.Subtotal).ToString("C");

    private sealed record MedicineOption(int Id, string Name, decimal UnitPrice, int Available)
    {
        public string Display => $"{Name} — {UnitPrice:C} ({Available} available)";
    }
}

public sealed class CartLine(int medicineId, string name, int available, int quantity, decimal unitPrice) : INotifyPropertyChanged
{
    public int MedicineId { get; } = medicineId;
    public string Name { get; } = name;
    public int Available { get; } = available;
    public decimal UnitPrice { get; } = unitPrice;

    private int _quantity = quantity;
    public int Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value) return;
            _quantity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtotal));
        }
    }

    public decimal Subtotal => Quantity * UnitPrice;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
