using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Categories;
[Authorize]
public sealed class EditModel(MediStockDbContext db):PageModel{
 [BindProperty(SupportsGet=true)]public int Id{get;set;}
 [BindProperty]public CategoryInput Input{get;set;}=new();
 public async Task<IActionResult>OnGetAsync(CancellationToken ct){var x=await db.Categories.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==Id,ct);if(x is null)return NotFound();Input=new CategoryInput{Name=x.Name,Description=x.Description};return Page();}
 public async Task<IActionResult>OnPostAsync(CancellationToken ct){var x=await db.Categories.SingleOrDefaultAsync(x=>x.Id==Id,ct);if(x is null)return NotFound();if(!ModelState.IsValid)return Page();if(await db.Categories.AnyAsync(c=>c.Id!=Id&&c.Name.ToUpper()==Input.Name.Trim().ToUpper(),ct)){ModelState.AddModelError("Input.Name","A category with this name already exists.");return Page();}x.Name=Input.Name.Trim();x.Description=string.IsNullOrWhiteSpace(Input.Description)?null:Input.Description.Trim();await db.SaveChangesAsync(ct);TempData["StatusMessage"]="Category changes saved.";return RedirectToPage("Index");}
}
