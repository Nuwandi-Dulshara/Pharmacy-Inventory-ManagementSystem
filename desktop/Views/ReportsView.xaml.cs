using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MediStock;
using MediStock.Data;
using MediStock.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using QuestPDF.Infrastructure;

namespace MediStock.Desktop.Views;

public partial class ReportsView : UserControl
{
    private readonly DesktopServices _services;
    private readonly string _pharmacyName;
    private ReportData? _currentReport;

    public ReportsView(DesktopServices services, AuthenticatedUser user)
    {
        InitializeComponent();
        _services = services;
        _pharmacyName = services.GetSetting("PharmacyName", user.PharmacyName);
        FromPicker.SelectedDate = DateTime.Today.AddMonths(-1);
        ToPicker.SelectedDate = DateTime.Today;
        GenerateReport();
    }

    private void Generate_Click(object sender, RoutedEventArgs e) => GenerateReport();

    private void GenerateReport()
    {
        if (FromPicker.SelectedDate is not DateTime from || ToPicker.SelectedDate is not DateTime to || from.Date > to.Date)
        {
            StatusText.Text = "Select a valid date range with the start on or before the end.";
            return;
        }

        var type = (ReportTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Sales";
        var start = from.Date;
        var endExclusive = to.Date.AddDays(1);
        var startUtc = ToUtcBoundary(start);
        var endUtcExclusive = ToUtcBoundary(endExclusive);
        var today = DateTime.UtcNow.Date;
        var warningDaysValue = _services.GetSetting("ExpiryWarningDays", "30");
        var warningDays = int.TryParse(warningDaysValue, out var days) && days > 0 ? days : 30;
        var expiryCutoff = today.AddDays(warningDays);

        try
        {
            using var db = _services.CreateDbContext();
            List<string> headers;
            List<IReadOnlyList<string>> rows;
            switch (type)
            {
                case "Stock":
                    headers = ["Medicine", "Batch", "Quantity", "Expiry", "Supplier"];
                    rows = db.StockBatches.AsNoTracking()
                        .Include(batch => batch.Medicine).Include(batch => batch.Supplier)
                        .Where(batch => batch.CreatedUtc >= startUtc && batch.CreatedUtc < endUtcExclusive)
                        .OrderBy(batch => batch.Medicine.Name).ToList()
                        .Select(batch => (IReadOnlyList<string>)
                        [
                            batch.Medicine.Name, batch.BatchNumber, batch.Quantity.ToString(),
                            batch.ExpiryDate.ToString("yyyy-MM-dd"), batch.Supplier.Name
                        ]).ToList();
                    break;
                case "Expiry":
                    headers = ["Medicine", "Batch", "Quantity", "Expiry", "Status"];
                    rows = db.StockBatches.AsNoTracking().Include(batch => batch.Medicine)
                        .Where(batch => batch.ExpiryDate >= start && batch.ExpiryDate < endExclusive)
                        .OrderBy(batch => batch.ExpiryDate).ToList()
                        .Select(batch => (IReadOnlyList<string>)
                        [
                            batch.Medicine.Name, batch.BatchNumber, batch.Quantity.ToString(),
                            batch.ExpiryDate.ToString("yyyy-MM-dd"),
                            batch.ExpiryDate.Date < today ? "Expired" :
                                batch.ExpiryDate.Date <= expiryCutoff ? "Expiring soon" : "Usable"
                        ]).ToList();
                    break;
                case "Supplier":
                    headers = ["Supplier", "Contact", "Phone", "Email", "Active"];
                    rows = db.Suppliers.AsNoTracking()
                        .Where(supplier => supplier.CreatedUtc >= startUtc && supplier.CreatedUtc < endUtcExclusive)
                        .OrderBy(supplier => supplier.Name).ToList()
                        .Select(supplier => (IReadOnlyList<string>)
                        [
                            supplier.Name, supplier.ContactPerson ?? string.Empty, supplier.Phone,
                            supplier.Email ?? string.Empty, supplier.IsActive ? "Yes" : "No"
                        ]).ToList();
                    break;
                default:
                    headers = ["Invoice", "Date", "Medicine", "Quantity", "Line total", "Payment", "Status"];
                    rows = db.SaleItems.AsNoTracking().Include(item => item.Sale).Include(item => item.Medicine)
                        .Where(item => item.Sale.SoldAtUtc >= startUtc && item.Sale.SoldAtUtc < endUtcExclusive)
                        .OrderByDescending(item => item.Sale.SoldAtUtc).ToList()
                        .Select(item => (IReadOnlyList<string>)
                        [
                            item.Sale.InvoiceNumber, item.Sale.SoldAtUtc.ToString("yyyy-MM-dd"),
                            item.Medicine.Name, item.Quantity.ToString(), item.LineTotal.ToString("0.00"),
                            item.Sale.PaymentMethod, item.Sale.Status
                        ]).ToList();
                    break;
            }

            _currentReport = new ReportData(type, start, to.Date, headers, rows);
            ReportGrid.ItemsSource = CreateTable(headers, rows).DefaultView;
            StatusText.Text = $"{rows.Count} row(s) in {type} report for {start:d} through {to:d}.";
        }
        catch (Exception exception) when (
            exception is SqliteException or InvalidOperationException)
        {
            _currentReport = null;
            ReportGrid.ItemsSource = null;
            StatusText.Text = $"Could not generate report: {exception.Message}";
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReport()) return;
        var report = _currentReport!;
        var dialog = new SaveFileDialog
        {
            Title = "Save report as CSV",
            FileName = $"{report.Type}-report-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}.csv",
            Filter = "CSV file (*.csv)|*.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", report.Headers.Select(EscapeCsv)));
            foreach (var row in report.Rows)
                csv.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText.Text = $"CSV report saved to {dialog.FileName}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not export CSV: {exception.Message}";
        }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureReport()) return;
        var report = _currentReport!;
        var dialog = new SaveFileDialog
        {
            Title = "Save report as PDF",
            FileName = $"{report.Type}-report-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}.pdf",
            Filter = "PDF file (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var bytes = new PdfExportService().GenerateReport(_pharmacyName, report.Type,
                report.From, report.To, report.Headers, report.Rows);
            File.WriteAllBytes(dialog.FileName, bytes);
            StatusText.Text = $"PDF report saved to {dialog.FileName}.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not export PDF: {exception.Message}";
        }
    }

    private bool EnsureReport()
    {
        GenerateReport();
        return _currentReport is not null;
    }

    private static DataTable CreateTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = new DataTable();
        foreach (var header in headers)
            table.Columns.Add(header);
        foreach (var row in rows)
            table.Rows.Add(row.Cast<object>().ToArray());
        return table;
    }

    private static string EscapeCsv(string value)
    {
        value = value.Replace("\"", "\"\"");
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value}\"" : value;
    }

    private static DateTime ToUtcBoundary(DateTime localDate) =>
        DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();

    private sealed record ReportData(
        string Type,
        DateTime From,
        DateTime To,
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<string>> Rows);
}
