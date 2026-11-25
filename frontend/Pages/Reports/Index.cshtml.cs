using System.Text;
using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Reports;

[Authorize]
public sealed class IndexModel(
    MediStockDbContext db,
    AppSettingsService settingsService,
    PdfExportService pdfExportService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string ReportType { get; set; } = "Sales";

    [BindProperty(SupportsGet = true)]
    public DateTime From { get; set; } = DateTime.UtcNow.AddMonths(-1);

    [BindProperty(SupportsGet = true)]
    public DateTime To { get; set; } = DateTime.UtcNow;

    public IReadOnlyList<string> Headers { get; private set; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCsvAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (ErrorMessage is not null)
        {
            return Page();
        }

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));
        foreach (var row in Rows)
        {
            csv.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"{ReportType}-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> OnPostPdfAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (ErrorMessage is not null)
        {
            return Page();
        }

        var settings = await settingsService.GetAllAsync(cancellationToken);
        var pharmacyName = settings.GetValueOrDefault("PharmacyName", "MediStock Pharmacy");
        var pdf = pdfExportService.GenerateReport(pharmacyName, ReportType, From.Date, To.Date, Headers, Rows);
        return File(pdf, "application/pdf", $"{ReportType}-report-{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (From.Date > To.Date)
        {
            ErrorMessage = "The start date must be on or before the end date.";
            return;
        }

        if (ReportType is not ("Sales" or "Stock" or "Expiry" or "Supplier"))
        {
            ErrorMessage = "Select a valid report type.";
            return;
        }

        var from = From.Date;
        var toExclusive = To.Date.AddDays(1);
        var fromUtc = ToUtcBoundary(from);
        var toUtcExclusive = ToUtcBoundary(toExclusive);
        var expiryWarningDays = await settingsService.GetIntAsync("ExpiryWarningDays", 30, cancellationToken);
        var today = DateTime.UtcNow.Date;
        var expiryCutoff = today.AddDays(expiryWarningDays);

        if (ReportType == "Stock")
        {
            Headers = ["Medicine", "Batch", "Quantity", "Expiry", "Supplier"];
            Rows = (await db.StockBatches.AsNoTracking()
                    .Where(batch => batch.CreatedUtc >= fromUtc && batch.CreatedUtc < toUtcExclusive)
                    .OrderBy(batch => batch.Medicine.Name)
                    .Select(batch => new[]
                    {
                        batch.Medicine.Name,
                        batch.BatchNumber,
                        batch.Quantity.ToString(),
                        batch.ExpiryDate.ToString("yyyy-MM-dd"),
                        batch.Supplier.Name
                    })
                    .ToListAsync(cancellationToken))
                .Select(row => (IReadOnlyList<string>)row)
                .ToList();
        }
        else if (ReportType == "Expiry")
        {
            Headers = ["Medicine", "Batch", "Quantity", "Expiry", "Status"];
            Rows = (await db.StockBatches.AsNoTracking()
                    .Where(batch => batch.ExpiryDate >= from && batch.ExpiryDate < toExclusive)
                    .OrderBy(batch => batch.ExpiryDate)
                    .Select(batch => new[]
                    {
                        batch.Medicine.Name,
                        batch.BatchNumber,
                        batch.Quantity.ToString(),
                        batch.ExpiryDate.ToString("yyyy-MM-dd"),
                        batch.ExpiryDate < today ? "Expired" : batch.ExpiryDate <= expiryCutoff ? "Expiring soon" : "Usable"
                    })
                    .ToListAsync(cancellationToken))
                .Select(row => (IReadOnlyList<string>)row)
                .ToList();
        }
        else if (ReportType == "Supplier")
        {
            Headers = ["Supplier", "Contact", "Phone", "Email", "Active"];
            Rows = (await db.Suppliers.AsNoTracking()
                    .Where(supplier => supplier.CreatedUtc >= fromUtc && supplier.CreatedUtc < toUtcExclusive)
                    .OrderBy(supplier => supplier.Name)
                    .Select(supplier => new[]
                    {
                        supplier.Name,
                        supplier.ContactPerson ?? "",
                        supplier.Phone,
                        supplier.Email ?? "",
                        supplier.IsActive ? "Yes" : "No"
                    })
                    .ToListAsync(cancellationToken))
                .Select(row => (IReadOnlyList<string>)row)
                .ToList();
        }
        else
        {
            Headers = ["Invoice", "Date", "Medicine", "Quantity", "Line total", "Payment", "Status"];
            Rows = (await db.SaleItems.AsNoTracking()
                    .Where(item => item.Sale.SoldAtUtc >= fromUtc && item.Sale.SoldAtUtc < toUtcExclusive)
                    .OrderByDescending(item => item.Sale.SoldAtUtc)
                    .Select(item => new[]
                    {
                        item.Sale.InvoiceNumber,
                        item.Sale.SoldAtUtc.ToString("yyyy-MM-dd"),
                        item.Medicine.Name,
                        item.Quantity.ToString(),
                        item.LineTotal.ToString("0.00"),
                        item.Sale.PaymentMethod,
                        item.Sale.Status
                    })
                    .ToListAsync(cancellationToken))
                .Select(row => (IReadOnlyList<string>)row)
                .ToList();
        }
    }

    private static string EscapeCsv(string value)
    {
        value = value.Replace("\"", "\"\"");
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value}\""
            : value;
    }

    private static DateTime ToUtcBoundary(DateTime localDate) =>
        DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();
}
