namespace SmartPS.Models.Reports;

public sealed record ReportFilter(
    ReportDateRange Range,
    int? VehicleTypeId = null,
    ReportCustomerGroup CustomerGroup = ReportCustomerGroup.All,
    int? ZoneId = null,
    ReportPeriodPreset Preset = ReportPeriodPreset.Custom)
{
    public bool HasSessionFilter => VehicleTypeId.HasValue || ZoneId.HasValue || CustomerGroup != ReportCustomerGroup.All;
}
