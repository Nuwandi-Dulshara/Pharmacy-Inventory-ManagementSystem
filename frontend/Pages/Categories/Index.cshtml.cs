using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Categories;

[Authorize]
public sealed class IndexModel(MediStockDbContext db) : PageModel
{
    [BindProperty(SupportsGet=true)] public string? Search { get; set; }
    public IReadOnlyList<CategoryItem> Items { get; private set; }=[];
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }
    public async Task OnGetAsync(CancellationToken ct) {
        var q=db.Categories.AsNoTracking();
        if(!string.IsNullOrWhiteSpace(Search)) q=q.Where(x=>EF.Functions.Like(x.Name,$"%{Search.Trim()}%"));
        Items=await q.OrderBy(x=>x.Name).Select(x=>new CategoryItem(x.Id,x.Name,x.Description,x.IsActive)).ToListAsync(ct);
    }
    public async Task<IActionResult> OnPostDeleteAsync(int id,CancellationToken ct)
    {
        var category=await db.Categories.SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(category is null){ErrorMessage="Category not found.";return RedirectToPage();}
        if(await db.Medicines.AnyAsync(x=>x.CategoryId==id,ct)){ErrorMessage="This category is used by medicines and cannot be deleted.";return RedirectToPage();}
        db.Categories.Remove(category); await db.SaveChangesAsync(ct); StatusMessage="Category deleted."; return RedirectToPage();
    }
}
public sealed record CategoryItem(int Id,string Name,string? Description,bool IsActive);
