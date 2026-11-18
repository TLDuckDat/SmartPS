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
        [ReportTextKeys.SeriesCash] = SKColor.Parse("#168A5B"),
        [ReportTextKeys.SeriesVietQr] = SKColor.Parse("#E8735A"),
        [ReportTextKeys.SeriesCard] = SKColor.Parse("#8E4479"),
        [ReportTextKeys.SeriesNetRevenue] = SKColor.Parse("#F0A63A"),
        [ReportTextKeys.SeriesCheckIns] = SKColor.Parse("#E8735A"),
        [ReportTextKeys.SeriesCheckOuts] = SKColor.Parse("#8E4479"),
        [ReportTextKeys.SeriesAvgCheckIns] = SKColor.Parse("#E8735A"),
        [ReportTextKeys.SeriesAvgCheckOuts] = SKColor.Parse("#F0A63A"),
        [ReportTextKeys.SeriesOccupied] = SKColor.Parse("#D9647A"),
        [ReportTextKeys.SeriesAvailable] = SKColor.Parse("#F2E2D4"),
        [ReportTextKeys.GroupResident] = SKColor.Parse("#8E4479"),
        [ReportTextKeys.GroupMonthlyPass] = SKColor.Parse("#168A5B"),
        [ReportTextKeys.GroupVisitor] = SKColor.Parse("#E8735A")
    };

    private static readonly SKColor[] Palette =
    {
        SKColor.Parse("#E8735A"), SKColor.Parse("#F0A63A"), SKColor.Parse("#8E4479"), SKColor.Parse("#168A5B"),
        SKColor.Parse("#D9647A"), SKColor.Parse("#3F2862"), SKColor.Parse("#C93B4B"), SKColor.Parse("#7A667C")
    };

    private static readonly SKColor LabelColor = SKColor.Parse("#564258");
    private static readonly SKColor GridColor = SKColor.Parse("#EFE0D6");

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

    /// <summary>Cột "xe đang gửi theo loại" trên Tổng quan; số liệu lấy từ CSDL (không cố định).</summary>
    public static ISeries[] CreateParkedByVehicleType(IReadOnlyList<SmartPS.Models.Reports.VehicleTypeCount> items, string seriesName)
    {
        if (items.Count == 0) return Array.Empty<ISeries>();

        return new ISeries[]
        {
            new ColumnSeries<int>
            {
                Name = seriesName,
                Values = items.Select(i => i.Count).ToArray(),
                Fill = new SolidColorPaint(Palette[0]),
                Stroke = null,
                MaxBarWidth = 56,
                DataLabelsPaint = new SolidColorPaint(LabelColor),
                DataLabelsSize = AxisTextSize,
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top
            }
        };
    }

    public static Axis[] CreateParkedCategoryAxes(IReadOnlyList<SmartPS.Models.Reports.VehicleTypeCount> items) => new[]
    {
        new Axis
        {
            Labels = items.Select(i => i.Name).ToArray(),
            LabelsPaint = new SolidColorPaint(LabelColor),
            TextSize = AxisTextSize,
            SeparatorsPaint = null
        }
    };

    public static Axis[] CreateParkedValueAxes() => new[]
    {
        new Axis
        {
            MinLimit = 0,
            MinStep = 1,
            Labeler = value => Math.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            LabelsPaint = new SolidColorPaint(LabelColor),
            TextSize = AxisTextSize,
            SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 1 }
        }
    };

    private static SKColor ColorFor(string nameKey, int index)
        => SeriesColors.TryGetValue(nameKey, out var color) ? color : Palette[index % Palette.Length];
}
