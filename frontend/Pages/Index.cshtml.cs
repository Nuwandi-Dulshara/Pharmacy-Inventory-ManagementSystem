using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MediStock.Pages;

public sealed class IndexModel : PageModel
{
    public IReadOnlyList<FeatureItem> Features { get; } =
    [
        new("✚", "Medicine Management", "Organize medicines, details, and suppliers in one place."),
        new("▤", "Stock Tracking", "Know what’s in stock and what needs reordering."),
        new("▱", "Sales", "Keep sales moving and daily activity easy to review."),
        new("◷", "Expiry Alerts", "Stay ahead of important dates with timely reminders."),
        new("✧", "AI Predictions", "Plan ahead with estimates of future stock needs.")
    ];

    public sealed record FeatureItem(string Icon, string Title, string Description);
}
