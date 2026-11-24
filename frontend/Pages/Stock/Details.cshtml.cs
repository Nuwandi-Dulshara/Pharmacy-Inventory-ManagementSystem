using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Stock;

[Authorize]
public sealed class DetailsModel(MediStockDbContext db, AppSettingsService settingsService) : PageModel
{
    public StockBatch? Batch { get; private set; }
    public string Status { get; private set; } = "Usable";
    public int MedicineAvailableQuantity { get; private set; }
    public int LowStockQuantity { get; private set; }
    public int ExpiryWarningDays { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Batch = await db.StockBatches
            .Include(batch => batch.Medicine)
            .Include(batch => batch.Supplier)
            .AsNoTracking()
            .SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);
        if (Batch is null)
        {
            return NotFound();
        }

        LowStockQuantity = await settingsService.GetIntAsync("LowStockQuantity", 10, cancellationToken);
        ExpiryWarningDays = await settingsService.GetIntAsync("ExpiryWarningDays", 30, cancellationToken);
        var today = DateTime.UtcNow.Date;
        var expiryCutoff = today.AddDays(ExpiryWarningDays);
        Status = Batch.ExpiryDate.Date < today
            ? "Expired"
            : Batch.ExpiryDate.Date <= expiryCutoff ? "Expiring soon" : "Usable";
        MedicineAvailableQuantity = await db.StockBatches
            .AsNoTracking()
            .Where(batch => batch.MedicineId == Batch.MedicineId &&
                            batch.Quantity > 0 &&
                            batch.ExpiryDate >= today)
            .SumAsync(batch => (int?)batch.Quantity, cancellationToken) ?? 0;
        return Page();
    }
}
