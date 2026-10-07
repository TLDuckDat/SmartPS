namespace SmartPS.Models.GateControl;

/// <summary>Nhóm xe khi vào bãi, quyết định luồng cấp ô và cách tính phí.</summary>
public enum VehicleCategory
{
    Visitor = 0,      // Khách vãng lai
    MonthlyPass = 1,  // Vé tháng (không phải cư dân)
    Resident = 2,     // Cư dân có vé tháng còn hạn
    Blacklisted = 3   // Biển số trong danh sách đen
}
