using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Expiry;

[Authorize]
public sealed class IndexModel(MediStockDbContext db, AppSettingsService settingsService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Filter { get; set; }

    public IReadOnlyList<ExpiryRow> Items { get; private set; } = [];
    public int ExpiryWarningDays { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        ExpiryWarningDays = await settingsService.GetIntAsync("ExpiryWarningDays", 30, cancellationToken);
        var cutoff = today.AddDays(ExpiryWarningDays);
        var query = db.StockBatches.AsNoTracking().Where(batch => batch.Quantity > 0);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(batch => EF.Functions.Like(batch.Medicine.Name, $"%{Search.Trim()}%"));
        }

        if (Filter == "expired")
        {
            query = query.Where(batch => batch.ExpiryDate < today);
        }
        else if (Filter == "soon")
        {
            query = query.Where(batch => batch.ExpiryDate >= today && batch.ExpiryDate <= cutoff);
        }

        Items = await query
            .OrderBy(batch => batch.ExpiryDate)
            .Select(batch => new ExpiryRow(
                batch.Id,
                batch.Medicine.Name,
                batch.BatchNumber,
                batch.Quantity,
                batch.ExpiryDate,
                batch.ExpiryDate < today ? "Expired" : batch.ExpiryDate <= cutoff ? "Expiring soon" : "Usable"))
            .ToListAsync(cancellationToken);
    }
}

public sealed record ExpiryRow(int Id, string Medicine, string BatchNumber, int Quantity, DateTime ExpiryDate, string Status);
