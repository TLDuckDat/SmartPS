using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SmartPS.Models.Reports;
using SmartPS.Services.Reports;

namespace SmartPS.ViewModels.Reports;

/// <summary>The only place that builds LiveCharts2 objects from the pure chart DTOs.</summary>
public static class LiveChartsSeriesFactory
{
    private const float AxisTextSize = 12;

    private static readonly Dictionary<string, SKColor> SeriesColors = new(StringComparer.Ordinal)
    {
        [ReportTextKeys.SeriesCash] = SKColor.Parse("#15803D"),
        [ReportTextKeys.SeriesVietQr] = SKColor.Parse("#1D4ED8"),
        [ReportTextKeys.SeriesCard] = SKColor.Parse("#7C3AED"),
        [ReportTextKeys.SeriesNetRevenue] = SKColor.Parse("#D97706"),
        [ReportTextKeys.SeriesCheckIns] = SKColor.Parse("#1D4ED8"),
        [ReportTextKeys.SeriesCheckOuts] = SKColor.Parse("#D97706"),
        [ReportTextKeys.SeriesAvgCheckIns] = SKColor.Parse("#1D4ED8"),
        [ReportTextKeys.SeriesAvgCheckOuts] = SKColor.Parse("#D97706"),
        [ReportTextKeys.SeriesOccupied] = SKColor.Parse("#0284C7"),
        [ReportTextKeys.SeriesAvailable] = SKColor.Parse("#E2E8F0"),
        [ReportTextKeys.GroupResident] = SKColor.Parse("#0284C7"),
        [ReportTextKeys.GroupMonthlyPass] = SKColor.Parse("#7C3AED"),
        [ReportTextKeys.GroupVisitor] = SKColor.Parse("#D97706")
    };

    private static readonly SKColor[] Palette =
    {
        SKColor.Parse("#1D4ED8"), SKColor.Parse("#15803D"), SKColor.Parse("#D97706"), SKColor.Parse("#7C3AED"),
        SKColor.Parse("#0284C7"), SKColor.Parse("#BE123C"), SKColor.Parse("#0F766E"), SKColor.Parse("#64748B")
    };

    private static readonly SKColor LabelColor = SKColor.Parse("#334155");
    private static readonly SKColor GridColor = SKColor.Parse("#E2E8F0");

    public static ISeries[] CreateCartesian(CartesianChartData data, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(localize);

        var series = new List<ISeries>(data.Series.Count);
        for (var i = 0; i < data.Series.Count; i++)
        {
            var source = data.Series[i];
            var color = ColorFor(source.NameKey, i);
            var name = localize(source.NameKey);
            var values = source.Values.ToArray();
            Func<LiveChartsCore.Kernel.ChartPoint, string> tooltip = data.IsCurrency
                ? point => $"{name}: {ReportFormat.Currency((decimal)point.Coordinate.PrimaryValue)}"
                : point => $"{name}: {ReportFormat.Number(point.Coordinate.PrimaryValue, 1)}";

            series.Add(source.Kind switch
            {
                ChartSeriesKind.StackedColumn => new StackedColumnSeries<double>
                {
                    Name = name,
                    Values = values,
                    Fill = new SolidColorPaint(color),
                    Stroke = null,
                    MaxBarWidth = 36,
                    YToolTipLabelFormatter = tooltip
                },
                ChartSeriesKind.Column => new ColumnSeries<double>
                {
                    Name = name,
                    Values = values,
                    Fill = new SolidColorPaint(color),
                    Stroke = null,
                    MaxBarWidth = 18,
                    YToolTipLabelFormatter = tooltip
                },
                ChartSeriesKind.Line => new LineSeries<double>
                {
                    Name = name,
                    Values = values,
                    Fill = null,
                    Stroke = new SolidColorPaint(color, 2),
                    GeometrySize = 7,
                    GeometryFill = new SolidColorPaint(color),
                    GeometryStroke = new SolidColorPaint(color, 2),
                    LineSmoothness = 0.2,
                    YToolTipLabelFormatter = tooltip
                },
                ChartSeriesKind.StackedRow => new StackedRowSeries<double>
                {
                    Name = name,
                    Values = values,
                    Fill = new SolidColorPaint(color),
                    Stroke = null,
                    MaxBarWidth = 28,
                    XToolTipLabelFormatter = tooltip
                },
                _ => throw new ArgumentOutOfRangeException(nameof(data), source.Kind, "Unknown series kind.")
            });
        }

        return series.ToArray();
    }

    /// <summary>One category axis carrying the labels (the X axis for columns/lines, the Y axis for row series).</summary>
    public static Axis[] CreateCategoryAxes(CartesianChartData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new[]
        {
            new Axis
            {
                Labels = data.Labels.ToArray(),
                TextSize = AxisTextSize,
                LabelsPaint = new SolidColorPaint(LabelColor),
                MinStep = 1,
                ForceStepToMin = data.Labels.Count <= 31
            }
        };
    }

    public static Axis[] CreateValueAxes(bool currency) => new[]
    {
        new Axis
        {
            Labeler = currency
                ? value => ReportFormat.Currency((decimal)value)
                : value => ReportFormat.Number(value, value % 1 == 0 ? 0 : 1),
            MinLimit = currency ? null : 0,
            TextSize = AxisTextSize,
            LabelsPaint = new SolidColorPaint(LabelColor),
            SeparatorsPaint = new SolidColorPaint(GridColor, 1)
        }
    };

    /// <summary>Donut chart: one <see cref="PieSeries{T}"/> per slice (each holds a single value).</summary>
    public static ISeries[] CreatePie(PieChartData data, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(localize);

        var series = new ISeries[data.Slices.Count];
        for (var i = 0; i < series.Length; i++)
        {
            var slice = data.Slices[i];
            var name = slice.NameKey is null ? slice.Name ?? string.Empty : localize(slice.NameKey);
            var color = slice.NameKey is not null && SeriesColors.TryGetValue(slice.NameKey, out var known) ? known : Palette[i % Palette.Length];
            series[i] = new PieSeries<double>
            {
                Name = name,
                Values = new[] { slice.Value },
                InnerRadius = 55,
                Fill = new SolidColorPaint(color),
                ToolTipLabelFormatter = point => $"{name}: {ReportFormat.Number(point.Coordinate.PrimaryValue)}"
            };
        }

        return series;
    }

    private static SKColor ColorFor(string nameKey, int index)
        => SeriesColors.TryGetValue(nameKey, out var color) ? color : Palette[index % Palette.Length];
}
