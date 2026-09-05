# Hướng Dẫn Quản Lý & Tự Động Tạo Database PostgreSQL Bằng EF Core Code First (Migrations)

Dự án **Smart Parking System (SmartPS)** sử dụng **PostgreSQL** (chạy qua Docker) kết hợp với **EF Core Code First (`Npgsql.EntityFrameworkCore.PostgreSQL`)** để tự động khởi tạo cơ sở dữ liệu và nạp dữ liệu ban đầu.

---

## I. Cấu Hình Docker PostgreSQL

Lệnh khởi chạy container PostgreSQL của hệ thống:
```powershell
docker run -d --name my-postgres `
  -e POSTGRES_USER=smartps `
  -e POSTGRES_PASSWORD=smartps `
  -e POSTGRES_DB=SmartPS `
  -p 5432:5432 `
  -v pgdata:/var/lib/postgresql/data `
  postgres:15.19-trixie
```

### Thông số kết nối:
- **Host**: `localhost`
- **Port**: `5432`
- **Database**: `SmartPS`
- **Username**: `smartps`
- **Password**: `smartps`

Chuỗi kết nối chuẩn trong `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=SmartPS;Username=smartps;Password=smartps;"
  }
}
```

---

## II. Phần Nào Trong Dự Án Tạo Database Trước?

Quy trình tự động kiểm tra và tạo database trên PostgreSQL:

```
[1. Khởi động Ứng dụng]
        ↓
[2. App.xaml.cs : OnStartup()]  <-- Điểm kích hoạt đầu tiên
        ↓
[3. DbInitializer.InitializeAsync()] <-- Thực thi lệnh tạo DB & bảng
        ↓
[4. context.Database.MigrateAsync()] <-- EF Core đọc Migrations của Npgsql
        ↓
[5. Áp dụng file InitialCreate.cs] <-- Tự động tạo các bảng trên PostgreSQL
        ↓
[6. Seed Data] <-- Nạp Permissions, Roles (Admin, Operator), User admin
        ↓
[7. Hiển thị màn hình LoginView]
```

### 1. File kích hoạt: `App.xaml.cs` (Dòng 49 - 62)
Khi ứng dụng khởi chạy, trước khi hiển thị màn hình Đăng nhập, hàm `OnStartup` sẽ lấy `SmartPsDbContext` từ DI container và gọi:
```csharp
// App.xaml.cs
try
{
    var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SmartPsDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    
    // TỰ ĐỘNG TẠO DATABASE POSTGRESQL & NẠP DỮ LIỆU BAN ĐẦU
    await DbInitializer.InitializeAsync(dbContext);
}
catch (Exception ex)
{
    // Bắt lỗi an toàn nếu Docker container chưa chạy, không làm crash app
    System.Diagnostics.Debug.WriteLine($"[SmartPS PostgreSQL Auto-DB Warning]: {ex.Message}");
}

// Hiển thị màn hình đăng nhập
var loginView = ServiceProvider.GetRequiredService<LoginView>();
loginView.Show();
```

---

### 2. File thực thi cốt lõi: `Data/DbInitializer.cs` (Dòng 10 - 29)
Nơi trực tiếp thực hiện lệnh migration lên PostgreSQL:
```csharp
// Data/DbInitializer.cs
public static async Task InitializeAsync(SmartPsDbContext context)
{
    // 1. TỰ ĐỘNG TẠO DATABASE VÀ CHẠY MIGRATION NPGSQL
    try
    {
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }
    }
    catch
    {
        await context.Database.EnsureCreatedAsync();
    }

    // 2. TỰ ĐỘNG NẠP DỮ LIỆU MẶC ĐỊNH (ROLES, PERMISSIONS, ADMIN USER)
    // ...
}
```

---

### 3. File định nghĩa cấu trúc bảng: Thư mục `Migrations/`
- File `20260905163048_InitialCreate.cs`: Chứa mã C# sinh bảng theo chuẩn **PostgreSQL Npgsql**:
  - Khóa chính tự tăng: `NpgsqlValueGenerationStrategy.IdentityByDefaultColumn`
  - Kiểu dữ liệu tương thích PostgreSQL: `integer`, `character varying`
  - Các bảng: `Roles`, `Permissions`, `RolePermissions`, `Users`
- File `SmartPsDbContextModelSnapshot.cs`: Snapshot toàn bộ Model Schema của Npgsql.

---

### 4. File hỗ trợ công cụ CLI: `Data/SmartPsDbContextFactory.cs`
Cài đặt `IDesignTimeDbContextFactory<SmartPsDbContext>` dùng `optionsBuilder.UseNpgsql(connectionString)` để công cụ `dotnet ef` đọc chuỗi kết nối và sinh file Migration chính xác cho PostgreSQL.

---

## III. Hướng Dẫn Khi Cần Thêm Bảng Mới Trong Tương Lai

Mỗi khi bạn thêm bảng mới (ví dụ `ParkingSlots` hoặc `VehicleTickets`):

1. Tạo Model mới trong thư mục `Models/`.
2. Khai báo `DbSet<...>` trong `Data/SmartPsDbContext.cs`.
3. Mở terminal và gõ:
   ```powershell
   dotnet ef migrations add AddParkingSlotsTable
   ```
4. Khởi động lại ứng dụng WPF: Hệ thống sẽ tự động cập nhật bảng mới vào PostgreSQL mà **không làm mất dữ liệu cũ**.
   - Hoặc cập nhật thủ công: `dotnet ef database update`.

---

## IV. Bảng Tra Cứu Các Lệnh Thường Dùng

| Lệnh | Ý nghĩa |
| :--- | :--- |
| `docker start my-postgres` | Khởi động container PostgreSQL nếu đang dừng |
| `docker ps` | Kiểm tra trạng thái container PostgreSQL |
| `dotnet ef migrations add <Ten>` | Tạo file migration mới ghi nhận thay đổi Model |
| `dotnet ef database update` | Đẩy toàn bộ migration chưa chạy vào PostgreSQL |
| `dotnet ef migrations remove` | Xóa migration vừa tạo gần nhất (khi chưa update vào DB) |
| `dotnet ef migrations list` | Liệt kê danh sách migration trong dự án |
