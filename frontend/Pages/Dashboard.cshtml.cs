using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MediStock.Services;

namespace MediStock.Pages;

[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class DashboardModel(DashboardService dashboardService) : PageModel
{
    public DashboardOverview Overview { get; private set; } = null!;
    public string SalesChartDataUrl { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Overview = await dashboardService.GetOverviewAsync(DateTime.UtcNow, cancellationToken);
        SalesChartDataUrl = MonthlySalesChartRenderer.RenderDataUrl(Overview.MonthlySales);
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Index");
    }
}
