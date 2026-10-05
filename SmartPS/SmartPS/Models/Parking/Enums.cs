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
    Card = 2         // Thẻ / Ví điện tử
}

public enum CustomerType
{
    Regular = 0,     // Khách lạ / Vãng lai
    Loyal = 1,       // Khách thân quen
    VIP = 2          // Khách VIP / Cư dân
}

public enum MonthlyTicketStatus
{
    Active = 0,      // Còn hiệu lực
    Expired = 1,     // Đã hết hạn
    Suspended = 2    // Tạm khóa
}

