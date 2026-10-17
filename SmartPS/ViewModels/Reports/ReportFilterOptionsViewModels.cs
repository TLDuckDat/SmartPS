using SmartPS.Models.Reports;

namespace SmartPS.ViewModels.Reports;

public sealed record ReportPresetOption(ReportPeriodPreset Preset, string Label);

public sealed record CustomerGroupOption(ReportCustomerGroup Group, string Label);

/// <summary>Vehicle-type or zone choice; a null <see cref="Id"/> is the "All" entry.</summary>
public sealed record LookupOption(int? Id, string Label);
