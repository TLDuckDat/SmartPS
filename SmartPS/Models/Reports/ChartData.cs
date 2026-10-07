namespace SmartPS.Models.Reports;

public sealed record ChartSeriesData(string NameKey, ChartSeriesKind Kind, IReadOnlyList<double> Values);

public sealed record CartesianChartData(IReadOnlyList<string> Labels, IReadOnlyList<ChartSeriesData> Series, bool IsCurrency)
{
    public bool HasData => Series.Any(s => s.Values.Any(v => v != 0));
}

public sealed record PieSliceData(string? NameKey, string? Name, double Value);

public sealed record PieChartData(IReadOnlyList<PieSliceData> Slices)
{
    public bool HasData => Slices.Any(s => s.Value > 0);
}
