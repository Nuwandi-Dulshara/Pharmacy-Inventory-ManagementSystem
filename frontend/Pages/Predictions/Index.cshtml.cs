using MediStock.Data;
using MediStock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Predictions;

[Authorize]
public sealed class IndexModel(
    MediStockDbContext db,
    DemandPredictionService predictionService,
    AppSettingsService settingsService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int MedicineId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Days { get; set; } = 7;

    public IReadOnlyList<SelectListItem> Medicines { get; private set; } = [];
    public DemandForecast? Forecast { get; private set; }
    public string? ChartDataUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Medicines = await db.Medicines
            .AsNoTracking()
            .Where(medicine => medicine.IsActive)
            .OrderBy(medicine => medicine.Name)
            .Select(medicine => new SelectListItem(medicine.Name, medicine.Id.ToString()))
            .ToListAsync(cancellationToken);

        if (Days is not (7 or 30))
        {
            Days = 7;
        }

        if (MedicineId == 0 && Medicines.Count > 0)
        {
            MedicineId = int.Parse(Medicines[0].Value!);
        }

        if (MedicineId <= 0 || !Medicines.Any(item => item.Value == MedicineId.ToString()))
        {
            return;
        }

        Forecast = await predictionService.PredictAsync(MedicineId, Days, DateTime.UtcNow, cancellationToken);
        if (Forecast.HasEnoughHistory)
        {
            var darkMode = string.Equals(
                await settingsService.GetStringAsync("Theme", "Light", cancellationToken),
                "Dark",
                StringComparison.OrdinalIgnoreCase);
            ChartDataUrl = DemandForecastChartRenderer.RenderDataUrl(Forecast.Points, darkMode);
        }
    }
}
