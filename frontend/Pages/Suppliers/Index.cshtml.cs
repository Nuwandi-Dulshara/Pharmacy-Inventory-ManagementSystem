using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace MediStock.Pages.Suppliers;
[Authorize]
public sealed class IndexModel(MediStockDbContext db):PageModel{
[BindProperty(SupportsGet=true)]public string? Search{get;set;}public IReadOnlyList<SupplierItem>Items{get;private set;}=[];[TempData]public string?StatusMessage{get;set;}[TempData]public string?ErrorMessage{get;set;}
public async Task OnGetAsync(CancellationToken ct){var q=db.Suppliers.AsNoTracking();if(!string.IsNullOrWhiteSpace(Search)){var s=Search.Trim();q=q.Where(x=>EF.Functions.Like(x.Name,$"%{s}%")||(x.Phone!=null&&EF.Functions.Like(x.Phone,$"%{s}%"))||(x.Email!=null&&EF.Functions.Like(x.Email,$"%{s}%")));}Items=await q.OrderBy(x=>x.Name).Select(x=>new SupplierItem(x.Id,x.Name,x.Phone,x.Email,x.Address)).ToListAsync(ct);}
public async Task<IActionResult>OnPostDeleteAsync(int id,CancellationToken ct){var x=await db.Suppliers.SingleOrDefaultAsync(x=>x.Id==id,ct);if(x is null){ErrorMessage="Supplier not found.";return RedirectToPage();}if(await db.StockBatches.AnyAsync(b=>b.SupplierId==id,ct)){x.IsActive=false;StatusMessage="Supplier deactivated because it is linked to stock records.";}else{db.Suppliers.Remove(x);StatusMessage="Supplier deleted.";}await db.SaveChangesAsync(ct);return RedirectToPage();}
}public sealed record SupplierItem(int Id,string Name,string Phone,string?Email,string?Address);
