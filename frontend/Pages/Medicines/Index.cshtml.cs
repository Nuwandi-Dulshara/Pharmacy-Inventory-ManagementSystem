using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Medicines;

[Authorize]
public sealed class IndexModel(
    MediStockDbContext dbContext,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<MedicineListItem> Medicines { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var query = dbContext.Medicines.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();
            query = query.Where(medicine =>
                EF.Functions.Like(medicine.Name, $"%{search}%") ||
                (medicine.GenericName != null && EF.Functions.Like(medicine.GenericName, $"%{search}%")) ||
                (medicine.Brand != null && EF.Functions.Like(medicine.Brand, $"%{search}%")) ||
                (medicine.Category != null && EF.Functions.Like(medicine.Category.Name, $"%{search}%")));
        }

        Medicines = await query
            .OrderBy(medicine => medicine.Name)
            .Select(medicine => new MedicineListItem(
                medicine.Id,
                medicine.Name,
                medicine.Category == null ? null : medicine.Category.Name,
                medicine.Brand,
                medicine.SellingPrice,
                medicine.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var medicine = await dbContext.Medicines
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (medicine is null)
        {
            ErrorMessage = "The medicine could not be found.";
            return RedirectToPage();
        }

        var hasSales = await dbContext.SaleItems.AnyAsync(item => item.MedicineId == id, cancellationToken);
        var hasBatches = await dbContext.StockBatches.AnyAsync(batch => batch.MedicineId == id, cancellationToken);
        var statusMessage = hasSales || hasBatches
            ? "This medicine is linked to stock or sales records, so it was deactivated instead of deleted."
            : "Medicine deleted.";
        if (hasSales || hasBatches)
        {
            medicine.IsActive = false;
        }
        else
        {
            dbContext.Medicines.Remove(medicine);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            StatusMessage = statusMessage;
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Could not delete or deactivate medicine {MedicineId}.", id);
            ErrorMessage = "The medicine could not be updated because it is linked to existing records.";
        }

        return RedirectToPage(new { Search });
    }
}

public sealed record MedicineListItem(
    int Id,
    string Name,
    string? Category,
    string? Brand,
    decimal SellingPrice,
    bool IsActive);
