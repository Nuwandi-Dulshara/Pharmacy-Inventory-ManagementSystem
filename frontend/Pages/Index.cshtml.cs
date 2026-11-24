using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediStock.Pages;

public sealed class IndexModel : PageModel
{
    public IReadOnlyList<FeatureItem> Features { get; } =
    [
        new("✚", "Medicine Management", "View medicine details and supplier information."),
        new("▤", "Stock Tracking", "View current stock levels and batch details."),
        new("▱", "Sales", "Record sales and review completed transactions."),
        new("◷", "Expiry Alerts", "Review expired and upcoming stock batches."),
        new("✧", "Demand Forecast", "View sales-based estimates of future stock needs.")
    ];

    public sealed record FeatureItem(string Icon, string Title, string Description);
}
