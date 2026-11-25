using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Medicines;

[Authorize]
public sealed class EditModel(
    MediStockDbContext dbContext,
    ILogger<EditModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public MedicineFormInput Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> Categories { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var medicine = await dbContext.Medicines
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == Id, cancellationToken);
        if (medicine is null)
        {
            return NotFound();
        }

        Input = new MedicineFormInput
        {
            Name = medicine.Name,
            GenericName = medicine.GenericName,
            CategoryId = medicine.CategoryId,
            Brand = medicine.Brand,
            SellingPrice = medicine.SellingPrice,
            Description = medicine.Description
        };
        await LoadCategoriesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var medicine = await dbContext.Medicines.SingleOrDefaultAsync(item => item.Id == Id, cancellationToken);
        if (medicine is null)
        {
            return NotFound();
        }

        if (Input.CategoryId.HasValue &&
            Input.CategoryId != medicine.CategoryId &&
            !await dbContext.Categories.AnyAsync(category =>
                category.Id == Input.CategoryId && category.IsActive, cancellationToken))
        {
            ModelState.AddModelError("Input.CategoryId", "Select an active category.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCategoriesAsync(cancellationToken);
            return Page();
        }

        medicine.Name = Input.Name.Trim();
        medicine.GenericName = Clean(Input.GenericName);
        medicine.CategoryId = Input.CategoryId;
        medicine.Brand = Clean(Input.Brand);
        medicine.SellingPrice = Input.SellingPrice;
        medicine.Description = Clean(Input.Description);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Could not update medicine {MedicineId}.", Id);
            ModelState.AddModelError(string.Empty, "The medicine could not be saved. Please try again.");
            await LoadCategoriesAsync(cancellationToken);
            return Page();
        }

        TempData["StatusMessage"] = "Medicine changes saved.";
        return RedirectToPage("Details", new { id = medicine.Id });
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        Categories = await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.IsActive || category.Id == Input.CategoryId)
            .OrderBy(category => category.Name)
            .Select(category => new SelectListItem(category.Name, category.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
