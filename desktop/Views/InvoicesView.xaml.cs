using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MediStock;
using MediStock.Data;
using MediStock.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using QuestPDF.Infrastructure;

namespace MediStock.Desktop.Views;

public partial class InvoicesView : UserControl
{
    private readonly DesktopServices _services;
    private readonly AuthenticatedUser _user;
    private readonly string _email;
    private List<InvoiceRow> _allInvoices = [];

    public InvoicesView(DesktopServices services, AuthenticatedUser user, string email)
    {
        InitializeComponent();
        _services = services;
        _user = user;
        _email = email;
        RefundPanel.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private bool IsAdministrator => string.Equals(_user.Role, "Administrator", StringComparison.OrdinalIgnoreCase);

    private void Refresh()
    {
        try
        {
            using var db = _services.CreateDbContext();
            _allInvoices = db.Sales.AsNoTracking()
                .OrderByDescending(sale => sale.SoldAtUtc)
                .Select(sale => new InvoiceRow
                {
                    Id = sale.Id,
                    InvoiceNumber = sale.InvoiceNumber,
                    SoldAt = sale.SoldAtUtc,
                    PaymentMethod = sale.PaymentMethod,
                    Status = sale.Status,
                    Total = sale.TotalAmount
                })
                .ToList();
            ApplyFilters();
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not load invoices: {exception.Message}";
        }
    }

    private void ApplyFilters()
    {
        if (FromDatePicker.SelectedDate is DateTime fromDate &&
            ToDatePicker.SelectedDate is DateTime toDate &&
            fromDate.Date > toDate.Date)
        {
            InvoicesGrid.ItemsSource = null;
            StatusText.Text = "The start date must be on or before the end date.";
            return;
        }

        var query = _allInvoices.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            var search = SearchBox.Text.Trim();
            query = query.Where(invoice => invoice.InvoiceNumber.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (FromDatePicker.SelectedDate is DateTime from)
        {
            query = query.Where(invoice =>
                DateTime.SpecifyKind(invoice.SoldAt, DateTimeKind.Utc).ToLocalTime().Date >= from.Date);
        }

        if (ToDatePicker.SelectedDate is DateTime to)
        {
            query = query.Where(invoice =>
                DateTime.SpecifyKind(invoice.SoldAt, DateTimeKind.Utc).ToLocalTime().Date <= to.Date);
        }

        InvoicesGrid.ItemsSource = query.ToList();
        ItemsGrid.ItemsSource = null;
        HistoryText.Text = string.Empty;
        StatusText.Text = $"{InvoicesGrid.Items.Count} invoice(s)";
    }

    private void Search_Click(object sender, RoutedEventArgs e) => ApplyFilters();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        ApplyFilters();
    }

    private void InvoicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InvoicesGrid.SelectedItem is not InvoiceRow invoice)
        {
            ItemsGrid.ItemsSource = null;
            HistoryText.Text = string.Empty;
            return;
        }

        var sale = LoadSale(invoice.Id);
        if (sale is null)
        {
            StatusText.Text = "The selected invoice no longer exists.";
            ItemsGrid.ItemsSource = null;
            return;
        }

        ItemsGrid.ItemsSource = sale.Items.Select(item => new InvoiceItemRow
        {
            Medicine = item.Medicine.Name,
            Batch = item.StockBatch.BatchNumber,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            LineTotal = item.LineTotal
        }).ToList();
        HistoryText.Text = sale.RefundHistory is { } history
            ? $"{history.Action} on {history.CreatedUtc.ToLocalTime():g} · {history.Amount:C} · " +
              $"{history.AuthorizedUser} ({history.AuthorizedUserEmail}) · Reason: {history.Reason}"
            : "No refund or void history.";
        RefundPanel.Visibility = IsAdministrator && sale.Status == "Completed"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var sale = GetSelectedSale();
        if (sale is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save invoice as PDF",
            FileName = $"{sale.InvoiceNumber}.pdf",
            Filter = "PDF file (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var lines = sale.Items.Select(item => new PdfInvoiceLine(
                item.Medicine.Name,
                item.StockBatch.BatchNumber,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal)).ToList();
            var pdf = new PdfExportService().GenerateInvoice(
                _services.GetSetting("PharmacyName", _user.PharmacyName),
                _services.GetSetting("PharmacyAddress", string.Empty),
                _services.GetSetting("PharmacyContact", string.Empty),
                sale.InvoiceNumber,
                sale.SoldAtUtc,
                sale.PaymentMethod,
                sale.TotalAmount,
                lines,
                _services.GetSetting("Currency", "LKR"));
            System.IO.File.WriteAllBytes(dialog.FileName, pdf);
            StatusText.Text = $"Invoice PDF saved to {dialog.FileName}.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not export the invoice PDF: {exception.Message}";
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var sale = GetSelectedSale();
        if (sale is null)
        {
            return;
        }

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        var currency = _services.GetSetting("Currency", "LKR");
        var document = new FlowDocument
        {
            PagePadding = new Thickness(36),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            ColumnWidth = printDialog.PrintableAreaWidth
        };
        document.Blocks.Add(new Paragraph(new Run(_services.GetSetting("PharmacyName", _user.PharmacyName)))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold
        });
        document.Blocks.Add(new Paragraph(new Run(_services.GetSetting("PharmacyAddress", string.Empty))));
        document.Blocks.Add(new Paragraph(new Run(_services.GetSetting("PharmacyContact", string.Empty))));
        document.Blocks.Add(new Paragraph(new Run(
            $"Invoice {sale.InvoiceNumber} · {sale.SoldAtUtc.ToLocalTime():g} · {sale.PaymentMethod}")));

        var table = new Table();
        for (var index = 0; index < 5; index++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        var header = new TableRow();
        foreach (var label in new[] { "Medicine", "Batch", "Qty", "Unit price", "Subtotal" })
        {
            header.Cells.Add(new TableCell(new Paragraph(new Run(label)) { FontWeight = FontWeights.Bold }));
        }

        group.Rows.Add(header);
        foreach (var item in sale.Items)
        {
            var row = new TableRow();
            foreach (var text in new[]
                     {
                         item.Medicine.Name,
                         item.StockBatch.BatchNumber,
                         item.Quantity.ToString(),
                         $"{currency} {item.UnitPrice:N2}",
                         $"{currency} {item.LineTotal:N2}"
                     })
            {
                row.Cells.Add(new TableCell(new Paragraph(new Run(text))));
            }

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph(new Run($"Total: {currency} {sale.TotalAmount:N2}"))
        {
            TextAlignment = TextAlignment.Right,
            FontSize = 16,
            FontWeight = FontWeights.Bold
        });
        printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, sale.InvoiceNumber);
    }

    private void Refund_Click(object sender, RoutedEventArgs e) => ProcessRefundOrVoid("Refund");

    private void Void_Click(object sender, RoutedEventArgs e) => ProcessRefundOrVoid("Void");

    private void ProcessRefundOrVoid(string action)
    {
        if (!IsAdministrator || InvoicesGrid.SelectedItem is not InvoiceRow selected)
        {
            StatusText.Text = "Only administrators can refund or void sales.";
            return;
        }

        var reason = ReasonBox.Text.Trim();
        if (reason.Length is < 3 or > 500)
        {
            StatusText.Text = "Enter a reason between 3 and 500 characters.";
            return;
        }

        if (MessageBox.Show(
                $"{action} invoice {selected.InvoiceNumber} and restore its stock?",
                $"Confirm {action.ToLowerInvariant()}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var db = _services.CreateDbContext();
            db.Database.OpenConnection();
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            db.Database.UseTransaction(transaction);
            var sale = db.Sales.Include(item => item.Items)
                .SingleOrDefault(item => item.Id == selected.Id);
            if (sale is null || sale.Status != "Completed" ||
                db.RefundHistories.Any(history => history.SaleId == selected.Id))
            {
                StatusText.Text = "This sale no longer exists or has already been refunded or voided.";
                return;
            }

            var timestamp = DateTime.UtcNow;
            foreach (var item in sale.Items)
            {
                var batch = db.StockBatches.Single(stock => stock.Id == item.StockBatchId);
                batch.Quantity = checked(batch.Quantity + item.Quantity);
                db.StockAdjustments.Add(new StockAdjustment
                {
                    StockBatchId = batch.Id,
                    QuantityChange = item.Quantity,
                    Reason = $"{action} {sale.InvoiceNumber}: {reason}",
                    AdjustedAtUtc = timestamp
                });
            }

            sale.Status = action == "Void" ? "Voided" : "Refunded";
            sale.VoidReason = reason;
            sale.RefundedAtUtc = timestamp;
            db.RefundHistories.Add(new RefundHistory
            {
                SaleId = sale.Id,
                Action = action,
                Reason = reason,
                AuthorizedUser = _user.FullName,
                AuthorizedUserEmail = _email,
                Amount = sale.TotalAmount,
                CreatedUtc = timestamp
            });
            db.SaveChanges();
            transaction.Commit();
            ReasonBox.Clear();
            Refresh();
            InvoicesGrid.SelectedItem = _allInvoices.SingleOrDefault(invoice => invoice.Id == selected.Id);
            StatusText.Text = $"Sale {action.ToLowerInvariant()}ed; stock restored.";
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException or OverflowException)
        {
            StatusText.Text = $"Could not process the {action.ToLowerInvariant()}: {exception.Message}";
        }
    }

    private Sale? GetSelectedSale()
    {
        if (InvoicesGrid.SelectedItem is not InvoiceRow selected)
        {
            StatusText.Text = "Select an invoice first.";
            return null;
        }

        var sale = LoadSale(selected.Id);
        if (sale is null)
        {
            StatusText.Text = "The selected invoice no longer exists.";
        }

        return sale;
    }

    private Sale? LoadSale(int saleId)
    {
        try
        {
            using var db = _services.CreateDbContext();
            return db.Sales.AsNoTracking()
                .Include(sale => sale.Items).ThenInclude(item => item.Medicine)
                .Include(sale => sale.Items).ThenInclude(item => item.StockBatch)
                .Include(sale => sale.RefundHistory)
                .SingleOrDefault(sale => sale.Id == saleId);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not load invoice details: {exception.Message}";
            return null;
        }
    }

    private sealed class InvoiceRow
    {
        public int Id { get; init; }
        public string InvoiceNumber { get; init; } = string.Empty;
        public DateTime SoldAt { get; init; }
        public string PaymentMethod { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public decimal Total { get; init; }
    }

    private sealed class InvoiceItemRow
    {
        public string Medicine { get; init; } = string.Empty;
        public string Batch { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal LineTotal { get; init; }
    }
}
