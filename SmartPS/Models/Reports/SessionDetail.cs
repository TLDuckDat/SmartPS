using SmartPS.Models.Parking;

namespace SmartPS.Models.Reports;

public sealed record SessionDetailRow(
    int SessionId, string TicketCode, string LicensePlate, string VehicleTypeName, ReportCustomerGroup CustomerGroup,
    string? ZoneName, string? SlotCode, DateTime CheckInUtc, DateTime? CheckOutUtc, double? DurationMinutes,
    SessionStatus Status, decimal TotalFee, PaymentMethod PaymentMethod);

public sealed record SessionDetailPage(IReadOnlyList<SessionDetailRow> Rows, int TotalCount, bool IsTruncated);
