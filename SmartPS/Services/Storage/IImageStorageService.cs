namespace SmartPS.Services.Storage;

/// <summary>
/// Dịch vụ quản lý và lưu trữ vĩnh viễn hình ảnh camera và biển số xe vào/ra.
/// Đảm bảo bằng chứng hình ảnh không bị ghi đè hay mất khi các tệp tạm thời hoặc tệp camera xoay vòng.
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// Lưu trữ an toàn một tệp hình ảnh vào kho lưu trữ có cấu trúc theo ngày và biển số
    /// </summary>
    /// <param name="sourceImagePath">Đường dẫn tệp ảnh gốc</param>
    /// <param name="category">Phân loại ảnh ('CheckIn' hoặc 'CheckOut')</param>
    /// <param name="licensePlate">Biển số xe tương ứng</param>
    /// <returns>Đường dẫn tệp ảnh đã lưu trữ an toàn, hoặc null nếu ảnh gốc không tồn tại</returns>
    Task<string?> ArchiveCaptureAsync(string? sourceImagePath, string category, string licensePlate);
}
