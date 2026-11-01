using SmartPS.Models.Parking;

namespace SmartPS.Models.Reports;

public sealed record ReportFilter(
    ReportDateRange Range,
    int? VehicleTypeId = null,
    ReportCustomerGroup CustomerGroup = ReportCustomerGroup.All,
    int? ZoneId = null,
    ReportPeriodPreset Preset = ReportPeriodPreset.Custom,
    int? StaffUserId = null,
    int? ShiftId = null,
    PaymentMethod? PaymentMethod = null)
{
    public bool HasSessionFilter => VehicleTypeId.HasValue || ZoneId.HasValue || CustomerGroup != ReportCustomerGroup.All || StaffUserId.HasValue || ShiftId.HasValue || PaymentMethod.HasValue;
}
