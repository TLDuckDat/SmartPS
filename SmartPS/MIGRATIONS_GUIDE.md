# Hướng Dẫn Vận Hành & Tự Động Tạo CSDL PostgreSQL Cho SmartPS

Hệ thống **Smart Parking System (SmartPS)** sử dụng **Entity Framework Core 10** kết hợp với **PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`)** theo mô hình **Code First**.

---

## I. Khởi Chạy Cơ Sở Dữ Liệu PostgreSQL

Bạn có thể chạy nhanh CSDL bằng Docker qua file `docker-compose.yml` có sẵn trong thư mục dự án:

```powershell
# Chạy container ngầm trong nền
docker compose up -d

# Kiểm tra container đã hoạt động
docker ps
```

Hoặc dùng lệnh Docker đơn lẻ:
```powershell
docker run -d --name smartps-postgres `
  -e POSTGRES_USER=smartps `
  -e POSTGRES_PASSWORD=smartps `
  -e POSTGRES_DB=SmartPS `
  -p 5432:5432 `
  -v pgdata:/var/lib/postgresql/data `
  postgres:16-alpine
```

### Thông số kết nối chuẩn (`appsettings.json`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=SmartPS;Username=smartps;Password=smartps;"
  }
}
```

---

## II. Cơ Chế Tự Động Tạo Database (Code First)

Khi bạn khởi động ứng dụng WPF:
1. **`App.xaml.cs` (`OnStartup`)**:
   Lấy `SmartPsDbContext` từ DI Container và gọi:
   ```csharp
   await DbInitializer.InitializeAsync(dbContext);
   ```
2. **`Data/DbInitializer.cs`**:
   Thực thi lệnh cốt lõi:
   ```csharp
   await context.Database.MigrateAsync();
   ```
   Lệnh này thực hiện hoàn toàn tự động:
   - Kết nối tới PostgreSQL Server.
   - Kiểm tra nếu database `SmartPS` chưa tồn tại -> **Tự động tạo mới database `SmartPS`**.
   - Kiểm tra và tạo bảng lịch sử `__EFMigrationsHistory`.
   - Áp dụng bản Migration `InitialCreate` để tạo đầy đủ **13 bảng CSDL**.

---

## III. Nạp Dữ Liệu Mẫu (Seed Data) Bằng File SQL

Toàn bộ dữ liệu mẫu đã được tách riêng vào file [`seed_data.sql`](./seed_data.sql) tại thư mục gốc dự án. Bạn có thể chủ động nạp dữ liệu vào database bất kỳ lúc nào:

### Cách 1: Nạp nhanh qua dòng lệnh Docker (Khuyên dùng)
```powershell
docker exec -i smartps-postgres psql -U smartps -d SmartPS < seed_data.sql
```

### Cách 2: Nạp qua công cụ quản trị (pgAdmin / DBeaver / Navicat / DataGrip)
1. Mở công cụ quản trị và kết nối vào CSDL `SmartPS`.
2. Mở file `seed_data.sql` và nhấn **Execute Script** (F5).

### Nội dung dữ liệu được nạp từ `seed_data.sql`:
- **Phân quyền & Tài khoản**:
  - 13 Quyền hệ thống (`Permissions`).
  - 3 Vai trò: `Admin`, `Manager`, `Operator` (`Roles`).
  - Phân quyền mặc định cho `Admin` (toàn quyền) và `Operator` (`RolePermissions`).
  - Tài khoản Quản trị viên: `admin` / Mật khẩu: `Admin@123` (đã hash chuẩn BCrypt).
- **Nghiệp vụ Bãi đỗ xe & Bảng giá**:
  - 3 Loại phương tiện (`VehicleTypes`): `Xe máy`, `Xe ô tô`, `Xe đạp / Xe điện`.
  - 3 Bảng giá mặc định (`PricingRules`): Giá 2h đầu, giá giờ tiếp theo và phụ thu qua đêm.
  - 3 Hạng khách hàng (`CustomerTiers`): Khách Vãng Lai (0%), Khách Thân Quen (-10%), VIP/Cư Dân (-20%).
  - 2 Khu vực đỗ xe mẫu (`ParkingZones`): Khu A (Tầng B1 - Xe máy) và Khu B (Tầng B2 - Ô tô).
  - 16 Vị trí đỗ (`ParkingSlots`): Ô `A-01` -> `A-10` và `B-01` -> `B-06` kèm tọa độ hiển thị 2D Canvas.
  - 4 Gói vé tháng mẫu (`MonthlyTicketPlans`): Gói xe máy/ô tô 1 tháng và 3 tháng tiết kiệm.

---

## IV. Danh Sách 13 Bảng CSDL

| STT | Tên Bảng | Mô Tả |
| :---: | :--- | :--- |
| 1 | `Permissions` | Danh mục quyền hạn trong hệ thống |
| 2 | `Roles` | Vai trò người dùng (Admin, Manager, Operator) |
| 3 | `RolePermissions` | Bảng quan hệ nhiều - nhiều giữa Role và Permission |
| 4 | `Users` | Tài khoản nhân viên / quản trị viên |
| 5 | `VehicleTypes` | Danh mục loại phương tiện (Xe máy, Ô tô,...) |
| 6 | `PricingRules` | Cấu hình biểu phí gửi xe theo lượt & qua đêm |
| 7 | `CustomerTiers` | Cấu hình hạng thành viên và % chiết khấu |
| 8 | `Customers` | Hồ sơ khách hàng / chủ phương tiện |
| 9 | `ParkingZones` | Khu vực bãi xe (Khu A, Khu B,...) |
| 10 | `ParkingSlots` | Vị trí ô đỗ cụ thể trên sơ đồ 2D |
| 11 | `MonthlyTicketPlans` | Gói đăng ký vé tháng |
| 12 | `MonthlyTickets` | Danh sách vé tháng khách hàng đã đăng ký |
| 13 | `ParkingSessions` | Phiên gửi xe thực tế (Check-in, Check-out, camera, tính tiền) |

---

## V. Các Lệnh EF Core Thường Dùng

Dự án đã tích hợp sẵn công cụ `dotnet-ef` cục bộ qua tệp `dotnet-tools.json`:

```powershell
# Khôi phục công cụ nếu clone sang máy mới
dotnet tool restore

# Liệt kê các migration hiện có
dotnet dotnet-ef migrations list

# Cập nhật CSDL thủ công từ terminal (thay vì chạy qua app)
dotnet dotnet-ef database update

# Khi bạn thêm hoặc sửa Model mới, gõ lệnh để tạo migration kế tiếp:
dotnet dotnet-ef migrations add TenMigrationMoi

# Xóa migration gần nhất (chưa đẩy vào DB)
dotnet dotnet-ef migrations remove
```
