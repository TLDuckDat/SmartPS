using SmartPS.Constants;
using SmartPS.Models.Audit;
using SmartPS.Services.Audit;

namespace SmartPS.Services.GateControl;

/// <summary>Các bản ghi nhật ký kiểm toán của luồng cổng liên quan đến danh sách đen.</summary>
public static class GateAuditEntries
{
    /// <summary>
    /// Ghi nhận xe bị chặn vì nằm trong danh sách đen. Lý do (văn bản tự do của người nhập) không được
    /// sao chép vào nhật ký bất biến để tránh lưu dữ liệu cá nhân.
    /// </summary>
    public static AuditEntry BlacklistBlocked(string licensePlate, string normalizedPlate, bool hadValidTicket)
    {
        return new AuditEntry(
            AuditActions.GateBlacklistBlocked,
            AuditOutcome.Denied,
            "LicensePlate",
            normalizedPlate,
            new
            {
                LicensePlate = licensePlate,
                NormalizedPlate = normalizedPlate,
                HadValidTicket = hadValidTicket
            });
    }
}
