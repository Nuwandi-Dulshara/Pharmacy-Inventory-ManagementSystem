using System.Security.Claims;
using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Invoices;

[Authorize]
public sealed class DetailsModel(
    MediStockDbContext db,
    AppSettingsService settingsService,
    PdfExportService pdfExportService,
    ILogger<DetailsModel> logger) : PageModel
{
    public Sale? Sale { get; private set; }
    public IReadOnlyList<InvoiceItem> Items { get; private set; } = [];
    public RefundHistory? RefundHistory { get; private set; }
    public string PharmacyName { get; private set; } = "MediStock Pharmacy";
    public string PharmacyAddress { get; private set; } = "";
    public string PharmacyContact { get; private set; } = "";
    public string Currency { get; private set; } = "LKR";

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var sale = Sale!;
        var lines = Items.Select(item => new PdfInvoiceLine(
            item.Medicine,
            item.Batch,
            item.Quantity,
            item.UnitPrice,
            item.LineTotal)).ToList();
        var pdf = pdfExportService.GenerateInvoice(
            PharmacyName,
            PharmacyAddress,
            PharmacyContact,
            sale.InvoiceNumber,
            sale.SoldAtUtc,
            sale.PaymentMethod,
            sale.TotalAmount,
            lines,
            Currency);
        return File(pdf, "application/pdf", $"{sale.InvoiceNumber}.pdf");
    }

    public Task<IActionResult> OnPostVoidAsync(int id, string reason, CancellationToken cancellationToken) =>
        ProcessRefundOrVoidAsync(id, "Void", reason, cancellationToken);

    public Task<IActionResult> OnPostRefundAsync(int id, string reason, CancellationToken cancellationToken) =>
        ProcessRefundOrVoidAsync(id, "Refund", reason, cancellationToken);

    private async Task<IActionResult> ProcessRefundOrVoidAsync(
        int id,
        string action,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Administrator"))
        {
            return Forbid();
        }

        var email = User.FindFirstValue(ClaimTypes.Email);
        var name = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
        {
            return Forbid();
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 500)
        {
            TempData["ErrorMessage"] = "Enter a reason between 3 and 500 characters.";
            return RedirectToPage(new { id });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var sale = await db.Sales
                .Include(item => item.Items)
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (sale is null)
            {
                return NotFound();
            }

            if (sale.Status != "Completed" ||
                await db.RefundHistories.AnyAsync(history => history.SaleId == id, cancellationToken))
            {
                TempData["ErrorMessage"] = "This sale has already been refunded or voided.";
                return RedirectToPage(new { id });
            }

            var timestamp = DateTime.UtcNow;
            foreach (var item in sale.Items)
            {
                var batch = await db.StockBatches.SingleAsync(
                    stockBatch => stockBatch.Id == item.StockBatchId,
                    cancellationToken);
                batch.Quantity = checked(batch.Quantity + item.Quantity);
                db.StockAdjustments.Add(new StockAdjustment
                {
                    StockBatchId = batch.Id,
                    QuantityChange = item.Quantity,
                    Reason = $"{action} {sale.InvoiceNumber}: {reason}",
                    AdjustedAtUtc = timestamp
                });
            }

            sale.Status = action == "Void" ? "Voided" : "Refunded";
            sale.VoidReason = reason;
            sale.RefundedAtUtc = timestamp;
            db.RefundHistories.Add(new RefundHistory
            {
                SaleId = sale.Id,
                Action = action,
                Reason = reason,
                AuthorizedUser = name,
                AuthorizedUserEmail = email,
                Amount = sale.TotalAmount,
                CreatedUtc = timestamp
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            TempData["StatusMessage"] = $"Sale {action.ToLowerInvariant()}ed and stock restored.";
            return RedirectToPage(new { id });
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Could not record {Action} for sale {SaleId}.", action, id);
            TempData["ErrorMessage"] = "The sale could not be updated. No stock was restored.";
            return RedirectToPage(new { id });
        }
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        Sale = await db.Sales
            .AsNoTracking()
            .Include(sale => sale.Items)
            .ThenInclude(item => item.Medicine)
            .Include(sale => sale.Items)
            .ThenInclude(item => item.StockBatch)
            .Include(sale => sale.RefundHistory)
            .SingleOrDefaultAsync(sale => sale.Id == id, cancellationToken);
        if (Sale is null)
        {
            return false;
        }

        Items = Sale.Items.Select(item =>
            new InvoiceItem(item.Medicine.Name, item.StockBatch.BatchNumber, item.Quantity, item.UnitPrice, item.LineTotal))
            .ToList();
        var settings = await settingsService.GetAllAsync(cancellationToken);
        PharmacyName = GetSetting(settings, "PharmacyName", PharmacyName);
        PharmacyAddress = GetSetting(settings, "PharmacyAddress", PharmacyAddress);
        PharmacyContact = GetSetting(settings, "PharmacyContact", PharmacyContact);
        Currency = GetSetting(settings, "Currency", Currency);
        RefundHistory = Sale.RefundHistory;
        return true;
    }

    private static string GetSetting(IReadOnlyDictionary<string, string> settings, string key, string fallback) =>
        settings.TryGetValue(key, out var value) ? value : fallback;
}

public sealed record InvoiceItem(string Medicine, string Batch, int Quantity, decimal UnitPrice, decimal LineTotal);
