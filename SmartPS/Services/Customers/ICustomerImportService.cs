// Hợp đồng cho nghiệp vụ đọc, kiểm tra và lưu dữ liệu import hộ dân/phương tiện.

using SmartPS.DTOs.Customers;

namespace SmartPS.Services.Customers;

public interface ICustomerImportService
{
    Task<CustomerImportPreview> PreviewAsync(
        string filePath,
        IProgress<CustomerImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<CustomerImportSaveResult> SaveAsync(
        CustomerImportPreview preview,
        IProgress<CustomerImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
