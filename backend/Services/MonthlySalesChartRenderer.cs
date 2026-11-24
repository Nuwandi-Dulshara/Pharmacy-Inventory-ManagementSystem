using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using MediStock.Services;
using SkiaSharp;

namespace MediStock;

public static class MonthlySalesChartRenderer
{
    public static string RenderDataUrl(IReadOnlyList<MonthlySalesPoint> data, bool darkMode = false)
    {
        var chart = new SKCartesianChart
        {
            Width = 900,
            Height = 300,
            Background = darkMode ? new SKColor(24, 37, 29) : SKColors.White,
            Series =
            [
                new ColumnSeries<double>
                {
                    Name = "Sales revenue",
                    Values = data.Select(point => (double)point.Amount).ToArray(),
                    Fill = new SolidColorPaint(darkMode ? new SKColor(112, 184, 135) : new SKColor(23, 76, 55)),
                    Stroke = null
                }
            ],
            XAxes =
            [
                new Axis
                {
                    Labels = data.Select(point => point.Month).ToArray(),
                    TextSize = 10,
                    LabelsRotation = 0,
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
