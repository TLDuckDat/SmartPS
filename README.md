# SmartPS – Hệ thống quản lý bãi đỗ xe thông minh

SmartPS là ứng dụng desktop (WPF, .NET 10) dùng để vận hành bãi đỗ xe: soát vé xe vào/ra, tính phí theo bảng giá, thanh toán tiền mặt và VietQR (PayOS), quản lý khách hàng và vé tháng, quản lý ca trực, báo cáo và nhật ký kiểm toán.

## Tính năng chính

| Nhóm | Mô tả |
|---|---|
| **Cổng vào/ra** | Soát vé xe vào, xe ra, tính cước, chặn xe trong danh sách đen, hiển thị nhãn cư dân |
| **Bảng giá** | Tính cước theo chu kỳ 24h, ân hạn, phụ phí qua đêm, giá vượt định mức; lưu phiên bản bảng giá và bản chụp giá khi xe ra |
| **Thanh toán** | Tiền mặt và VietQR qua PayOS (có chế độ Mock để chạy thử), webhook xác nhận thanh toán |
| **Khách hàng** | Hộ dân, khách hàng, phương tiện, vé tháng (cấp, gia hạn, đổi biển, khoá/huỷ), lịch sử vé, danh sách đen, import hộ dân và phương tiện |
| **Ca trực** | Mở/đóng ca, đối soát, điều chỉnh, duyệt ca |
| **Giao dịch** | Xem giao dịch của tôi, lọc theo ngày, trạng thái, phương thức, ca |
| **Báo cáo** | Biểu đồ, KPI, xem trước, in và xuất Excel/CSV/PDF (có thanh tiến trình và huỷ khi xuất Excel) |
| **Phân quyền** | Vai trò Admin, Manager, Operator, phân quyền chi tiết theo chức năng |
| **Nhật ký** | Ghi nhật ký kiểm toán mọi thao tác quan trọng, có kiểm tra toàn vẹn |
| **Khác** | Sự cố, cấu hình hệ thống, hồ sơ cá nhân và đổi mật khẩu, ghi nhớ đăng nhập (Windows DPAPI), đa ngôn ngữ (Tiếng Việt, English, 日本語) |

## Công nghệ

- .NET 10, WPF (MVVM), Microsoft.Extensions.DependencyInjection
- Entity Framework Core 10 + SQL Server (mặc định LocalDB); ADO.NET cho màn Nhật ký
- BCrypt.Net-Next (băm mật khẩu), QRCoder (VietQR), LiveCharts2 (biểu đồ)
- xUnit cho unit test và integration test

## Yêu cầu

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server LocalDB (cài kèm Visual Studio, hoặc qua SQL Server Express), hoặc một SQL Server khác

## Cài đặt và chạy

```bash
git clone https://github.com/TLDuckDat/SmartPS.git
cd SmartPS
dotnet restore
dotnet run --project SmartPS
```

Ở lần chạy đầu tiên, ứng dụng tự áp dụng migrations và chạy `seed_data.sql` để tạo quyền, vai trò, tài khoản mặc định và dữ liệu bãi xe. Chạy lại nhiều lần không bị trùng dữ liệu.

### Tài khoản mặc định

| Tên đăng nhập | Mật khẩu | Vai trò |
|---|---|---|
| `admin` | `Admin@123` | Admin |

Hãy đổi mật khẩu sau khi đăng nhập lần đầu.

## Cấu hình

Cấu hình chung nằm trong [SmartPS/appsettings.json](SmartPS/appsettings.json). Không sửa file này cho cấu hình riêng của máy bạn. Hãy tạo `SmartPS/appsettings.local.json` (đã được git bỏ qua) từ file mẫu [appsettings.local.json.example](SmartPS/appsettings.local.json.example). File local sẽ ghi đè lên `appsettings.json`.

Ví dụ dùng SQL Server Express và khoá PayOS thật:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SQLEXPRESS;Database=SmartPS;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "PayOS": {
    "UseMock": false,
    "ClientId": "...",
    "ApiKey": "...",
    "ChecksumKey": "..."
  }
}
```

| Khoá | Ý nghĩa |
|---|---|
| `ConnectionStrings:DefaultConnection` | Chuỗi kết nối SQL Server (mặc định `(localdb)\MSSQLLocalDB`, database `SmartPS`) |
| `PayOS:UseMock` | `true` để giả lập thanh toán, không gọi PayOS thật |
| `PayOS:ClientId`, `ApiKey`, `ChecksumKey` | Khoá PayOS, **không commit lên git** |
| `PaymentWebhook:Prefix` | Địa chỉ ứng dụng lắng nghe webhook thanh toán |

## Database và migrations

Dự án dùng công cụ `dotnet-ef` cục bộ (khai báo trong `SmartPS/dotnet-tools.json`).

```bash
cd SmartPS
dotnet tool restore

# Tạo migration mới sau khi đổi model
dotnet ef migrations add TenMigration

# Áp dụng migrations thủ công (ứng dụng cũng tự áp dụng khi khởi động)
dotnet ef database update
```

Khi hai nhánh cùng thêm migration, sau khi merge hãy kiểm tra không còn thay đổi model chờ:

```bash
dotnet ef migrations has-pending-model-changes
```

## Chạy test

```bash
dotnet test
```

Integration test dùng database `smartps_test` trên LocalDB. Để dùng máy chủ khác, đặt biến môi trường `SMARTPS_TEST_CONNECTION`:

```powershell
$env:SMARTPS_TEST_CONNECTION = "Server=.\SQLEXPRESS;Database=smartps_test;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test
```

Test hiệu năng (`ClassificationPerformanceTests`) có thể thỉnh thoảng fail khi máy đang tải nặng. Nếu gặp, hãy chạy lại riêng test đó.

## Cấu trúc thư mục

```
SmartPS/
├── SmartPS/                 # Ứng dụng WPF
│   ├── Constants/           # Hằng số quyền, action nhật ký
│   ├── Data/                # DbContext, cấu hình entity, khởi tạo DB
│   ├── DTOs/                # Đối tượng truyền dữ liệu
│   ├── Migrations/          # EF Core migrations
│   ├── Models/              # Entity
│   ├── Resources/Languages/ # Chuỗi đa ngôn ngữ (vi-VN, en-US, ja-JP)
│   ├── Services/            # Nghiệp vụ (Auth, Pricing, Payment, Reports, ...)
│   ├── ViewModels/          # MVVM ViewModel
│   ├── Views/               # Giao diện XAML
│   └── seed_data.sql        # Dữ liệu khởi tạo
├── SmartPS.Tests/           # Unit, Integration, Performance tests
└── SmartPS.slnx
```

## Quy ước làm việc với git

- Nhánh chính: `main`. Làm tính năng trên nhánh riêng: `feature/...`, `feat/...`, `fix/...`.
- Commit theo dạng Conventional Commits, ví dụ `feat(customers): ...`, `fix(pricing): ...`, `test: ...`.
- Trước khi tạo pull request: `dotnet build` không lỗi và `dotnet test` pass.
- Không commit `appsettings.local.json`, khoá PayOS hay chuỗi kết nối của máy cá nhân.
