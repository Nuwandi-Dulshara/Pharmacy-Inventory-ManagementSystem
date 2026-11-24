using System.ComponentModel.DataAnnotations;
using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Sales;

[Authorize]
public sealed class CreateModel(MediStockDbContext db, ILogger<CreateModel> logger) : PageModel
{
    [BindProperty]
    public SaleInput Input { get; set; } = new();

    public IReadOnlyList<AvailableMedicine> Medicines { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (Input.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one medicine to the cart.");
        }

        if (Input.Items.GroupBy(item => item.MedicineId).Any(group => group.Count() > 1))
        {
            ModelState.AddModelError(string.Empty, "A medicine can only appear once in the cart.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var sale = new Sale
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                SoldAtUtc = DateTime.UtcNow,
                PaymentMethod = Input.PaymentMethod,
                Status = "Completed"
            };
            decimal total = 0;
            foreach (var requestedItem in Input.Items)
            {
                var medicine = await db.Medicines
                    .SingleOrDefaultAsync(
                        item => item.Id == requestedItem.MedicineId && item.IsActive,
                        cancellationToken);
                if (medicine is null)
                {
                    ModelState.AddModelError(string.Empty, "One of the selected medicines is no longer available.");
                    await transaction.RollbackAsync(cancellationToken);
                    return Page();
                }

                var batches = await db.StockBatches
                    .Where(batch => batch.MedicineId == medicine.Id &&
                                    batch.Quantity > 0 &&
                                    batch.ExpiryDate >= DateTime.UtcNow.Date)
                    .OrderBy(batch => batch.ExpiryDate)
                    .ThenBy(batch => batch.Id)
                    .ToListAsync(cancellationToken);

                var remaining = requestedItem.Quantity;
                foreach (var batch in batches)
                {
                    if (remaining == 0)
                    {
                        break;
                    }

                    var quantity = Math.Min(remaining, batch.Quantity);
                    var lineTotal = quantity * medicine.SellingPrice;
                    sale.Items.Add(new SaleItem
                    {
                        MedicineId = medicine.Id,
                        StockBatchId = batch.Id,
                        Quantity = quantity,
                        UnitPrice = medicine.SellingPrice,
                        LineTotal = lineTotal
                    });
                    batch.Quantity -= quantity;
                    remaining -= quantity;
                    total += lineTotal;
                }

                if (remaining > 0)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"Insufficient non-expired stock for {medicine.Name}. No cart items were sold.");
                    await transaction.RollbackAsync(cancellationToken);
                    return Page();
                }
            }

            sale.TotalAmount = total;
            db.Sales.Add(sale);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            TempData["StatusMessage"] = "Sale completed.";
            return RedirectToPage("/Invoices/Details", new { id = sale.Id });
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Could not complete POS sale.");
            ModelState.AddModelError(string.Empty, "The sale could not be completed. No cart items were sold.");
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        Medicines = await db.Medicines
            .AsNoTracking()
            .Where(medicine => medicine.IsActive &&
                               db.StockBatches.Any(batch =>
                                   batch.MedicineId == medicine.Id &&
                                   batch.Quantity > 0 &&
                                   batch.ExpiryDate >= today))
            .OrderBy(medicine => medicine.Name)
            .Select(medicine => new AvailableMedicine(
                medicine.Id,
                medicine.Name,
                medicine.SellingPrice,
                db.StockBatches
                    .Where(batch => batch.MedicineId == medicine.Id &&
                                    batch.Quantity > 0 &&
                                    batch.ExpiryDate >= today)
                    .Sum(batch => (int?)batch.Quantity) ?? 0))
            .ToListAsync(cancellationToken);
    }
}

public sealed class SaleInput
{
    [Required]
    [RegularExpression("^(Cash|Card)$")]
    public string PaymentMethod { get; set; } = "Cash";

    public List<SaleLineInput> Items { get; set; } = [];
}

public sealed class SaleLineInput
{
    [Range(1, int.MaxValue)]
    public int MedicineId { get; set; }

    [Range(1, 1000000)]
    public int Quantity { get; set; }
}

public sealed record AvailableMedicine(int Id, string Name, decimal UnitPrice, int AvailableQuantity);
