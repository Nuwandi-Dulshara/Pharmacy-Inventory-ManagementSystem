using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MediStock.Data;
using MediStock.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop.Views;

public partial class PredictionsView : UserControl
{
    private readonly DesktopServices _services;

    public PredictionsView(DesktopServices services)
    {
        InitializeComponent();
        _services = services;
        try
        {
            using var db = services.CreateDbContext();
            var medicines = db.Medicines.AsNoTracking().Where(medicine => medicine.IsActive)
                .OrderBy(medicine => medicine.Name)
                .Select(medicine => new MedicineOption(medicine.Id, medicine.Name))
                .ToList();
            MedicineBox.ItemsSource = medicines;
            MedicineBox.SelectedIndex = medicines.Count > 0 ? 0 : -1;
            if (medicines.Count == 0)
                SummaryText.Text = "No active medicines are available for prediction.";
        }
        catch (Exception exception) when (
            exception is SqliteException or InvalidOperationException)
        {
            SummaryText.Text = $"Could not load medicines: {exception.Message}";
        }
    }

    private async void Predict_Click(object sender, RoutedEventArgs e)
    {
        if (MedicineBox.SelectedItem is not MedicineOption medicine ||
            DaysBox.SelectedItem is not ComboBoxItem { Tag: string daysText } ||
            !int.TryParse(daysText, out var days))
        {
            SummaryText.Text = "Select a medicine and a forecast range.";
            return;
        }

        PredictButton.IsEnabled = false;
        SummaryText.Text = "Analyzing sales history…";
        ForecastGrid.ItemsSource = null;
        ForecastChart.Source = null;
        ForecastChart.Visibility = Visibility.Collapsed;
        try
        {
            using var db = _services.CreateDbContext();
            var service = new DemandPredictionService(db);
            var forecast = await service.PredictAsync(medicine.Id, days, DateTime.UtcNow, CancellationToken.None);
            if (!forecast.HasEnoughHistory)
            {
                SummaryText.Text = $"Insufficient history for a reliable estimate ({forecast.ActiveSalesDays} active sales day(s)). " +
                                   $"Available unexpired stock: {forecast.CurrentStock}.";
                return;
            }

            ForecastGrid.ItemsSource = forecast.Points;
            var darkMode = string.Equals(
                _services.GetSetting("Theme", "Light"),
                "Dark",
                StringComparison.OrdinalIgnoreCase);
            var chartDataUrl = DemandForecastChartRenderer.RenderDataUrl(forecast.Points, darkMode);
            var chartBytes = Convert.FromBase64String(chartDataUrl[(chartDataUrl.IndexOf(',') + 1)..]);
            using (var stream = new MemoryStream(chartBytes))
            {
                var chartImage = new BitmapImage();
                chartImage.BeginInit();
                chartImage.CacheOption = BitmapCacheOption.OnLoad;
                chartImage.StreamSource = stream;
                chartImage.EndInit();
                chartImage.Freeze();
                ForecastChart.Source = chartImage;
            }
            ForecastChart.Visibility = Visibility.Visible;
            var shortage = forecast.PredictedQuantity > forecast.StockAvailableThroughForecast;
            SummaryText.Text = $"Estimated demand: {forecast.PredictedQuantity} units over {days} days. " +
                               $"Stock available through the forecast: {forecast.StockAvailableThroughForecast}. " +
                               (shortage ? "Potential stock shortage — review this estimate and reorder if appropriate."
                                   : "Current stock covers the estimate.");
            SummaryText.Foreground = shortage
                ? (System.Windows.Media.Brush)Application.Current.Resources["DangerBrush"]
                : (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"];
        }
        catch (Exception exception) when (
            exception is SqliteException or InvalidOperationException or ArgumentException or FormatException)
        {
            SummaryText.Text = $"Could not calculate prediction: {exception.Message}";
        }
        finally
        {
            PredictButton.IsEnabled = true;
        }
    }

    private sealed record MedicineOption(int Id, string Name)
    {
        public string Display => Name;
    }
}
