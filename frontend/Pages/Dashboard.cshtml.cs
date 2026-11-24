using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MediStock.Services;

namespace MediStock.Pages;

[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class DashboardModel(DashboardService dashboardService, AppSettingsService settingsService) : PageModel
{
    public DashboardOverview Overview { get; private set; } = null!;
    public string SalesChartDataUrl { get; private set; } = string.Empty;
    public bool IsDarkTheme { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Overview = await dashboardService.GetOverviewAsync(DateTime.UtcNow, cancellationToken);
        IsDarkTheme = string.Equals(
            await settingsService.GetStringAsync("Theme", "Light", cancellationToken),
            "Dark",
            StringComparison.OrdinalIgnoreCase);
        SalesChartDataUrl = MonthlySalesChartRenderer.RenderDataUrl(Overview.MonthlySales, IsDarkTheme);
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Index");
    }
}
