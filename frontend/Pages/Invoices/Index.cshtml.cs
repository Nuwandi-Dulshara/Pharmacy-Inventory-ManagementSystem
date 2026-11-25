using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Invoices;

[Authorize]
public sealed class IndexModel(MediStockDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? SelectedDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    public IReadOnlyList<InvoiceRow> Items { get; private set; } = [];
    public string? FilterError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var query = db.Sales.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();
            query = query.Where(sale => EF.Functions.Like(sale.InvoiceNumber, $"%{search}%"));
        }

        if (SelectedDate.HasValue)
        {
            var from = ToUtcBoundary(SelectedDate.Value);
            var to = ToUtcBoundary(SelectedDate.Value.Date.AddDays(1));
            query = query.Where(sale => sale.SoldAtUtc >= from && sale.SoldAtUtc < to);
        }

        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value.Date > ToDate.Value.Date)
        {
            FilterError = "The start date must be on or before the end date.";
            return;
        }

        if (FromDate.HasValue)
        {
            var from = ToUtcBoundary(FromDate.Value);
            query = query.Where(sale => sale.SoldAtUtc >= from);
        }

        if (ToDate.HasValue)
        {
            var toExclusive = ToUtcBoundary(ToDate.Value.Date.AddDays(1));
            query = query.Where(sale => sale.SoldAtUtc < toExclusive);
        }

        Items = await query
            .OrderByDescending(sale => sale.SoldAtUtc)
            .Select(sale => new InvoiceRow(
                sale.Id,
                sale.InvoiceNumber,
                sale.SoldAtUtc,
                sale.TotalAmount,
                sale.PaymentMethod,
                sale.Status))
            .ToListAsync(cancellationToken);
    }

    private static DateTime ToUtcBoundary(DateTime localDate) =>
        DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();
}

public sealed record InvoiceRow(int Id, string InvoiceNumber, DateTime Date, decimal Total, string PaymentMethod, string Status);
