using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using MediStock.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using SkiaSharp;

namespace MediStock.Services;

public sealed class DemandPredictionService(MediStockDbContext dbContext)
{
    private const int HistoryDays = 180;
    private const int MinimumTrainingRows = 14;
    private readonly MLContext _mlContext = new(seed: 42);

    public async Task<DemandForecast> PredictAsync(
        int medicineId,
        int forecastDays,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (forecastDays is not (7 or 30))
        {
            throw new ArgumentOutOfRangeException(nameof(forecastDays), "Forecasts must be 7 or 30 days.");
        }

        var today = utcNow.Date;
        var historyStart = today.AddDays(-HistoryDays);
        var sales = await dbContext.SaleItems
            .AsNoTracking()
            .Where(item => item.MedicineId == medicineId &&
                           item.Sale.Status == "Completed" &&
                           item.Sale.SoldAtUtc >= historyStart &&
                           item.Sale.SoldAtUtc < today)
            .Select(item => new { item.Sale.SoldAtUtc, item.Quantity })
            .ToListAsync(cancellationToken);

        var dailySales = sales
            .GroupBy(item => item.SoldAtUtc.Date)
            .ToDictionary(group => group.Key, group => (float)group.Sum(item => item.Quantity));
        var history = Enumerable.Range(0, HistoryDays)
            .Select(offset => dailySales.GetValueOrDefault(historyStart.AddDays(offset)))
            .ToArray();
        var trainingRows = Enumerable.Range(7, history.Length - 7)
            .Select(index => new DailyDemandRow
            {
                Features = history.Skip(index - 7).Take(7).ToArray(),
                Label = history[index]
            })
            .ToArray();
        var activeSalesDays = dailySales.Count;
        if (activeSalesDays < 7 || trainingRows.Length < MinimumTrainingRows)
        {
            var availableStock = await GetCurrentStockAsync(medicineId, today, cancellationToken);
            return new DemandForecast(false, [], 0, availableStock, availableStock, activeSalesDays);
        }

        var trainingData = _mlContext.Data.LoadFromEnumerable(trainingRows);
        var model = _mlContext.Regression.Trainers.Sdca(
                labelColumnName: nameof(DailyDemandRow.Label),
                featureColumnName: nameof(DailyDemandRow.Features))
            .Fit(trainingData);
        var predictionEngine = _mlContext.Model.CreatePredictionEngine<DailyDemandRow, DailyDemandPrediction>(model);
        var forecast = history.TakeLast(7).ToList();
        var points = new List<DemandForecastPoint>(forecastDays);
        for (var index = 0; index < forecastDays; index++)
        {
            var prediction = predictionEngine.Predict(new DailyDemandRow { Features = forecast.TakeLast(7).ToArray() });
            var units = Math.Max(0, prediction.Score);
            points.Add(new DemandForecastPoint(today.AddDays(index + 1), units));
            forecast.Add(units);
        }

        var currentStock = await GetCurrentStockAsync(medicineId, today, cancellationToken);
        var stockAvailableThroughForecast = await GetCurrentStockAsync(
            medicineId,
            today.AddDays(forecastDays),
            cancellationToken);
        var predictedQuantity = (int)Math.Ceiling(points.Sum(point => point.Units));
        return new DemandForecast(true, points, predictedQuantity, currentStock, stockAvailableThroughForecast, activeSalesDays);
    }

    private async Task<int> GetCurrentStockAsync(int medicineId, DateTime today, CancellationToken cancellationToken) =>
        await dbContext.StockBatches
            .AsNoTracking()
            .Where(batch => batch.MedicineId == medicineId &&
                            batch.Quantity > 0 &&
                            batch.ExpiryDate >= today)
            .SumAsync(batch => (int?)batch.Quantity, cancellationToken)
            ?? 0;
}

public sealed class DailyDemandRow
{
    [VectorType(7)]
    public float[] Features { get; set; } = [];
    public float Label { get; set; }
}

public sealed class DailyDemandPrediction
{
    [ColumnName("Score")]
    public float Score { get; set; }
}

public sealed record DemandForecast(
    bool HasEnoughHistory,
    IReadOnlyList<DemandForecastPoint> Points,
    int PredictedQuantity,
    int CurrentStock,
    int StockAvailableThroughForecast,
    int ActiveSalesDays);

public sealed record DemandForecastPoint(DateTime Date, float Units);

public static class DemandForecastChartRenderer
{
    public static string RenderDataUrl(IReadOnlyList<DemandForecastPoint> points, bool darkMode = false)
    {
        var chart = new SKCartesianChart
        {
            Width = 900,
            Height = 300,
            Background = darkMode ? new SKColor(24, 37, 29) : SKColors.White,
            Series =
            [
                new LineSeries<double>
                {
                    Name = "Estimated demand",
                    Values = points.Select(point => (double)point.Units).ToArray(),
                    GeometrySize = 8,
                    Fill = null,
                    Stroke = new SolidColorPaint(darkMode ? new SKColor(112, 184, 135) : new SKColor(23, 76, 55), 3)
                }
            ],
            XAxes =
            [
                new Axis
                {
                    Labels = points.Select(point => point.Date.ToString("dd MMM")).ToArray(),
                    LabelsRotation = 45,
                    TextSize = 10,
                    LabelsPaint = new SolidColorPaint(darkMode ? new SKColor(166, 181, 170) : new SKColor(113, 128, 119))
                }
            ],
            YAxes =
            [
                new Axis
                {
                    MinLimit = 0,
                    TextSize = 10,
                    Labeler = value => value.ToString("N0"),
                    LabelsPaint = new SolidColorPaint(darkMode ? new SKColor(166, 181, 170) : new SKColor(113, 128, 119))
                }
            ]
        };

        using var stream = new MemoryStream();
        chart.SaveImage(stream, SKEncodedImageFormat.Png, 100);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }
}
