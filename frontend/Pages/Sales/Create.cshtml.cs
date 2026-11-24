using System.ComponentModel.DataAnnotations;
using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Sales;

[Authorize]
public sealed class CreateModel(
    MediStockDbContext db,
    StockReservationCleanupService reservationCleanup,
    ILogger<CreateModel> logger) : PageModel
{
    private const string CartCookieName = "MediStock.SaleCart";
    private static readonly TimeSpan CartLifetime = StockReservationCleanupService.CartLifetime;

    [BindProperty]
    public SaleInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public string CartId { get; private set; } = string.Empty;
    public IReadOnlyList<AvailableMedicine> Medicines { get; private set; } = [];
    public IReadOnlyList<SaleCartLine> CartItems { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await reservationCleanup.ReleaseExpiredAsync(cancellationToken);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAddAsync(
        int medicineId,
        int quantity,
        CancellationToken cancellationToken)
    {
        await reservationCleanup.ReleaseExpiredAsync(cancellationToken);
        CartId = GetCartId();
        if (quantity is < 1 or > 1_000_000)
        {
            ModelState.AddModelError(string.Empty, "Enter a quantity greater than zero.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var medicine = await db.Medicines.SingleOrDefaultAsync(
                item => item.Id == medicineId && item.IsActive,
                cancellationToken);
            if (medicine is null)
            {
                ModelState.AddModelError(string.Empty, "The selected medicine is no longer available.");
                await transaction.RollbackAsync(cancellationToken);
                await LoadAsync(cancellationToken);
                return Page();
            }

            var existingReservations = await db.StockReservations
                .Where(reservation => reservation.CartId == CartId &&
                                      reservation.MedicineId == medicineId)
                .ToListAsync(cancellationToken);
            await RestoreReservationsAsync(existingReservations, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            var today = DateTime.UtcNow.Date;
            var batches = await db.StockBatches
                .Where(batch => batch.MedicineId == medicineId &&
                                batch.Quantity > 0 &&
                                batch.ExpiryDate >= today)
                .OrderBy(batch => batch.ExpiryDate)
                .ThenBy(batch => batch.Id)
                .ToListAsync(cancellationToken);

            var remaining = quantity;
            var reservedAtUtc = DateTime.UtcNow;
            foreach (var batch in batches)
            {
                if (remaining == 0)
                {
                    break;
                }

                var reservedQuantity = Math.Min(remaining, batch.Quantity);
                batch.Quantity -= reservedQuantity;
                db.StockReservations.Add(new StockReservation
                {
                    CartId = CartId,
                    MedicineId = medicineId,
                    StockBatchId = batch.Id,
                    Quantity = reservedQuantity,
                    UnitPrice = medicine.SellingPrice,
                    ReservedAtUtc = reservedAtUtc
                });
                remaining -= reservedQuantity;
            }

            if (remaining > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                ModelState.AddModelError(
                    string.Empty,
                    $"Only {quantity - remaining} units are available for {medicine.Name}.");
                await LoadAsync(cancellationToken);
                return Page();
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            StatusMessage = $"{medicine.Name} added to the cart.";
            return RedirectToPage(new { Search });
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Could not reserve stock for medicine {MedicineId}.", medicineId);
            ModelState.AddModelError(string.Empty, "Stock could not be reserved. Please try again.");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(int medicineId, CancellationToken cancellationToken)
    {
        await reservationCleanup.ReleaseExpiredAsync(cancellationToken);
        CartId = GetCartId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var reservations = await db.StockReservations
                .Where(reservation => reservation.CartId == CartId &&
                                      reservation.MedicineId == medicineId)
                .ToListAsync(cancellationToken);
            await RestoreReservationsAsync(reservations, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            StatusMessage = "Medicine removed and stock restored.";
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Could not release reserved medicine {MedicineId}.", medicineId);
            ModelState.AddModelError(string.Empty, "The medicine could not be removed from the cart.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage(new { Search });
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        await reservationCleanup.ReleaseExpiredAsync(cancellationToken);
        CartId = GetCartId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var reservations = await db.StockReservations
                .Where(reservation => reservation.CartId == CartId)
                .ToListAsync(cancellationToken);
            await RestoreReservationsAsync(reservations, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RedirectToPage("Index");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Could not release stock for cart {CartId}.", CartId);
            TempData["ErrorMessage"] = "The sale was not cancelled because its reserved stock could not be restored.";
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await reservationCleanup.ReleaseExpiredAsync(cancellationToken);
        CartId = GetCartId();
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var reservations = await db.StockReservations
                .Include(reservation => reservation.Medicine)
                .Include(reservation => reservation.StockBatch)
                .Where(reservation => reservation.CartId == CartId)
                .OrderBy(reservation => reservation.Medicine.Name)
                .ThenBy(reservation => reservation.StockBatchId)
                .ToListAsync(cancellationToken);
            if (reservations.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Add at least one medicine to the cart.");
                await transaction.RollbackAsync(cancellationToken);
                await LoadAsync(cancellationToken);
                return Page();
            }

            var today = DateTime.UtcNow.Date;
            if (reservations.Any(reservation =>
                    !reservation.Medicine.IsActive ||
                    reservation.StockBatch.ExpiryDate < today))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "A medicine in the cart is no longer available. Remove it before completing the sale.");
                await transaction.RollbackAsync(cancellationToken);
                await LoadAsync(cancellationToken);
                return Page();
            }

            var sale = new Sale
            {
                InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                SoldAtUtc = DateTime.UtcNow,
                PaymentMethod = Input.PaymentMethod,
                Status = "Completed"
            };
            foreach (var reservation in reservations)
            {
                sale.Items.Add(new SaleItem
                {
                    MedicineId = reservation.MedicineId,
                    StockBatchId = reservation.StockBatchId,
                    Quantity = reservation.Quantity,
                    UnitPrice = reservation.UnitPrice,
                    LineTotal = reservation.Quantity * reservation.UnitPrice
                });
                sale.TotalAmount += reservation.Quantity * reservation.UnitPrice;
            }

            db.Sales.Add(sale);
            db.StockReservations.RemoveRange(reservations);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            TempData["StatusMessage"] = "Sale completed.";
            return RedirectToPage("/Invoices/Details", new { id = sale.Id });
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Could not complete POS sale.");
            ModelState.AddModelError(string.Empty, "The sale could not be completed. Reserved stock remains in the cart.");
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CartId = GetCartId();
        var today = DateTime.UtcNow.Date;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var search = Search.Trim();
            Medicines = await db.Medicines
                .AsNoTracking()
                .Where(medicine => medicine.IsActive &&
                                   EF.Functions.Like(medicine.Name, $"%{search}%") &&
                                   db.StockBatches.Any(batch =>
                                       batch.MedicineId == medicine.Id &&
                                       batch.Quantity > 0 &&
                                       batch.ExpiryDate >= today))
                .OrderBy(medicine => medicine.Name)
                .Select(medicine => new AvailableMedicine(
                    medicine.Id,
                    medicine.Name,
                    medicine.SellingPrice,
                    (db.StockBatches
                         .Where(batch => batch.MedicineId == medicine.Id &&
                                         batch.Quantity > 0 &&
                                         batch.ExpiryDate >= today)
                         .Sum(batch => (int?)batch.Quantity) ?? 0) +
                    (db.StockReservations
                         .Where(reservation => reservation.CartId == CartId &&
                                               reservation.MedicineId == medicine.Id)
                         .Sum(reservation => (int?)reservation.Quantity) ?? 0)))
                .ToListAsync(cancellationToken);
        }

        var reservationLines = await db.StockReservations
            .AsNoTracking()
            .Where(reservation => reservation.CartId == CartId)
            .Select(reservation => new
            {
                reservation.MedicineId,
                Name = reservation.Medicine.Name,
                reservation.Quantity,
                reservation.UnitPrice
            })
            .ToListAsync(cancellationToken);
        CartItems = reservationLines
            .GroupBy(reservation => new { reservation.MedicineId, reservation.Name })
            .Select(group => new SaleCartLine(
                group.Key.MedicineId,
                group.Key.Name,
                group.Sum(reservation => reservation.Quantity),
                group.First().UnitPrice))
            .OrderBy(item => item.Name)
            .ToList();
    }

    private async Task RestoreReservationsAsync(
        IReadOnlyCollection<StockReservation> reservations,
        CancellationToken cancellationToken)
    {
        if (reservations.Count == 0)
        {
            return;
        }

        var quantitiesByBatch = reservations
            .GroupBy(reservation => reservation.StockBatchId)
            .Select(group => new { StockBatchId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToList();
        foreach (var item in quantitiesByBatch)
        {
            var batch = await db.StockBatches.SingleAsync(
                stockBatch => stockBatch.Id == item.StockBatchId,
                cancellationToken);
            batch.Quantity = checked(batch.Quantity + item.Quantity);
        }

        db.StockReservations.RemoveRange(reservations);
    }

    private string GetCartId()
    {
        if (Request.Cookies.TryGetValue(CartCookieName, out var existing) &&
            Guid.TryParseExact(existing, "N", out _))
        {
            return existing;
        }

        var cartId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(CartCookieName, cartId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            MaxAge = CartLifetime
        });
        return cartId;
    }
}

public sealed class SaleInput
{
    [Required]
    [RegularExpression("^(Cash|Card)$")]
    public string PaymentMethod { get; set; } = "Cash";
}

public sealed record AvailableMedicine(int Id, string Name, decimal UnitPrice, int AvailableQuantity);
public sealed record SaleCartLine(int MedicineId, string Name, int Quantity, decimal UnitPrice);
