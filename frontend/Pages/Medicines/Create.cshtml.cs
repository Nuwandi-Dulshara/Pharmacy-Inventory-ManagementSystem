using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MediStock.Pages.Medicines;

[Authorize]
public sealed class CreateModel(
    MediStockDbContext dbContext,
    ILogger<CreateModel> logger) : PageModel
{
    [BindProperty]
    public MedicineFormInput Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> Categories { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadCategoriesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Input.CategoryId.HasValue &&
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

        var medicine = new Medicine
        {
            Name = Input.Name.Trim(),
            GenericName = Clean(Input.GenericName),
            CategoryId = Input.CategoryId,
            Brand = Clean(Input.Brand),
            SellingPrice = Input.SellingPrice,
            Description = Clean(Input.Description),
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };

        dbContext.Medicines.Add(medicine);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Could not create medicine {MedicineName}.", Input.Name);
            ModelState.AddModelError(string.Empty, "The medicine could not be saved. Please try again.");
            await LoadCategoriesAsync(cancellationToken);
            return Page();
        }

        TempData["StatusMessage"] = "Medicine added.";
        return RedirectToPage("Details", new { id = medicine.Id });
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        Categories = await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Name)
            .Select(category => new SelectListItem(category.Name, category.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
