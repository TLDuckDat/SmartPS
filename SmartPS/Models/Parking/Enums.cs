namespace SmartPS.Models.Parking;

public enum SlotStatus
{
    Available = 0,   // Chỗ trống
    Occupied = 1,    // Đang có xe đỗ
    Maintenance = 2  // Tạm khóa / Bảo trì
}

public enum SessionStatus
{
    Active = 0,      // Đang đỗ trong bãi
    Completed = 1,   // Đã thanh toán & xuất bãi
    Cancelled = 2    // Hủy phiên
}

public enum PaymentMethod
{
    Cash = 0,        // Tiền mặt
    VietQR = 1,      // Chuyển khoản VietQR
    Card = 2,        // Thẻ / Ví điện tử
    Free = 3         // Miễn phí / vé tháng
}

public enum CustomerType
{
    Resident = 0,    // Cư dân
    External = 1     // Khách bên ngoài
}

public enum MonthlyTicketStatus
{
    Active = 0,      // Còn hiệu lực
    Expired = 1,     // Đã hết hạn
    Suspended = 2,   // Tạm khóa
    Cancelled = 3    // Đã hủy
}

