namespace SmartPS.Models.Reports;

public sealed record LookupItem(int Id, string Name);

public sealed record ReportFilterOptions(IReadOnlyList<LookupItem> VehicleTypes, IReadOnlyList<LookupItem> Zones);
