using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Medicines;

[Authorize]
public sealed class DetailsModel(MediStockDbContext dbContext) : PageModel
{
    public Medicine? Medicine { get; private set; }
    public IReadOnlyList<MedicineBatchDetails> Batches { get; private set; } = [];
    public int AvailableQuantity { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Medicine = await dbContext.Medicines
            .AsNoTracking()
            .Include(medicine => medicine.Category)
            .SingleOrDefaultAsync(medicine => medicine.Id == id, cancellationToken);
        if (Medicine is null)
        {
            return NotFound();
        }

        var today = DateTime.UtcNow.Date;
        Batches = await dbContext.StockBatches
            .AsNoTracking()
            .Where(batch => batch.MedicineId == id)
            .OrderBy(batch => batch.ExpiryDate)
            .Select(batch => new MedicineBatchDetails(
                batch.BatchNumber,
                batch.Quantity,
                batch.ExpiryDate,
                batch.Supplier.Name))
            .ToListAsync(cancellationToken);
        AvailableQuantity = await dbContext.StockBatches
            .AsNoTracking()
            .Where(batch => batch.MedicineId == id && batch.Quantity > 0 && batch.ExpiryDate >= today)
            .SumAsync(batch => (int?)batch.Quantity, cancellationToken) ?? 0;

        return Page();
    }
}

public sealed record MedicineBatchDetails(string BatchNumber, int Quantity, DateTime ExpiryDate, string SupplierName);
