using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Stock;

[Authorize]
public sealed class IndexModel(
    MediStockDbContext db,
    AppSettingsService settingsService,
    StockReservationCleanupService reservationCleanup,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public IReadOnlyList<StockItem> Items { get; private set; } = [];
    public int LowStockQuantity { get; private set; }
    public int ExpiryWarningDays { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await reservationCleanup.ReleaseExpiredAsync(cancellationToken);
        LowStockQuantity = await settingsService.GetIntAsync("LowStockQuantity", 10, cancellationToken);
        ExpiryWarningDays = await settingsService.GetIntAsync("ExpiryWarningDays", 30, cancellationToken);
        var today = DateTime.UtcNow.Date;
        var expiryCutoff = today.AddDays(ExpiryWarningDays);
        var query = db.StockBatches.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();
            query = query.Where(batch =>
                EF.Functions.Like(batch.Medicine.Name, $"%{search}%") ||
                EF.Functions.Like(batch.BatchNumber, $"%{search}%"));
        }

        if (Status == "expired")
        {
            query = query.Where(batch => batch.ExpiryDate < today);
        }
        else if (Status == "expiring")
        {
            query = query.Where(batch => batch.ExpiryDate >= today && batch.ExpiryDate <= expiryCutoff);
        }
        else if (Status == "usable")
        {
            query = query.Where(batch => batch.ExpiryDate >= today);
        }
        else if (Status == "low")
        {
            query = query.Where(batch =>
                db.StockBatches
                    .Where(available => available.MedicineId == batch.MedicineId &&
                                        available.Quantity > 0 &&
                                        available.ExpiryDate >= today)
                    .Sum(available => (int?)available.Quantity) <= LowStockQuantity);
        }

        Items = await query
            .OrderBy(batch => batch.ExpiryDate)
            .Select(batch => new StockItem(
                batch.Id,
                batch.Medicine.Name,
                batch.BatchNumber,
                batch.Quantity,
                batch.ExpiryDate,
                batch.Supplier.Name,
                batch.ExpiryDate < today ? "Expired" : batch.ExpiryDate <= expiryCutoff ? "Expiring soon" : "Usable",
                db.StockBatches
                    .Where(available => available.MedicineId == batch.MedicineId &&
                                        available.Quantity > 0 &&
                                        available.ExpiryDate >= today)
                    .Sum(available => (int?)available.Quantity) ?? 0,
                batch.Quantity == 0 &&
                !db.SaleItems.Any(item => item.StockBatchId == batch.Id) &&
                !db.StockReservations.Any(reservation => reservation.StockBatchId == batch.Id) &&
                !db.StockAdjustments.Any(adjustment => adjustment.StockBatchId == batch.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var batch = await db.StockBatches.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (batch is null)
        {
            ErrorMessage = "Stock batch not found.";
            return RedirectToPage(new { Search, Status });
        }

        if (batch.Quantity > 0 ||
            await db.SaleItems.AnyAsync(item => item.StockBatchId == id, cancellationToken) ||
            await db.StockReservations.AnyAsync(reservation => reservation.StockBatchId == id, cancellationToken) ||
            await db.StockAdjustments.AnyAsync(adjustment => adjustment.StockBatchId == id, cancellationToken))
        {
            ErrorMessage = "Used stock records cannot be deleted. Only unused, empty batches can be deleted.";
            return RedirectToPage(new { Search, Status });
        }

        try
        {
            db.StockBatches.Remove(batch);
            await db.SaveChangesAsync(cancellationToken);
            StatusMessage = "Unused stock batch deleted.";
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Could not delete stock batch {StockBatchId}.", id);
            ErrorMessage = "The stock batch could not be deleted because it is linked to existing records.";
        }

        return RedirectToPage(new { Search, Status });
    }
}

public sealed record StockItem(
    int Id,
    string Medicine,
    string BatchNumber,
    int Quantity,
    DateTime ExpiryDate,
    string Supplier,
    string Status,
    int MedicineAvailableQuantity,
    bool CanDelete);
