using System.ComponentModel.DataAnnotations;
using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Categories;
[Authorize]
public sealed class CreateModel(MediStockDbContext db):PageModel
{
 [BindProperty] public CategoryInput Input {get;set;}=new();
 public async Task<IActionResult> OnPostAsync(CancellationToken ct){
  if(!ModelState.IsValid)return Page();
  if(await db.Categories.AnyAsync(x=>x.Name.ToUpper()==Input.Name.Trim().ToUpper(),ct)){ModelState.AddModelError("Input.Name","A category with this name already exists.");return Page();}
  db.Categories.Add(new Category{Name=Input.Name.Trim(),Description=Clean(Input.Description),IsActive=true});await db.SaveChangesAsync(ct);TempData["StatusMessage"]="Category added.";return RedirectToPage("Index");
 }
 private static string? Clean(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
}
public sealed class CategoryInput{[Required, StringLength(120)]public string Name{get;set;}="";[StringLength(1000)]public string? Description{get;set;}}
