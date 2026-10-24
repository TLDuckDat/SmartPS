-- ===================================================================================
-- SMART PARKING SYSTEM (SmartPS) - SEED DATA SCRIPT (SQL Server / T-SQL)
-- File: seed_data.sql
-- Mô tả: Dữ liệu khởi tạo mặc định cho Phân quyền, Tài khoản Admin, Nghiệp vụ Bãi xe.
-- Ứng dụng tự chạy script này sau khi áp dụng migrations (DbInitializer); chạy lại nhiều lần không bị trùng.
-- Chạy tay: mở trong SSMS, chọn database SmartPS rồi Execute (F5).
-- Chuỗi tiếng Việt dùng tiền tố N'...' để lưu đúng Unicode vào cột nvarchar.
-- ===================================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- -----------------------------------------------------------------------------------
-- 1. DANH MỤC QUYỀN HẠN (Permissions)
-- -----------------------------------------------------------------------------------
INSERT INTO [Permissions] ([PermissionName], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    (1, N'User.View', N'Xem danh sách người dùng'),
    (2, N'User.Create', N'Tạo người dùng mới'),
    (3, N'User.Edit', N'Chỉnh sửa thông tin người dùng'),
    (4, N'User.Delete', N'Xóa người dùng'),
    (5, N'Role.View', N'Xem danh sách vai trò'),
    (6, N'Role.Manage', N'Quản lý phân quyền vai trò'),
    (7, N'Parking.View', N'Xem trạng thái bãi đỗ xe'),
    (8, N'Parking.CheckIn', N'Soát vé xe vào'),
    (9, N'Parking.CheckOut', N'Soát vé xe ra & tính phí'),
    (10, N'Parking.Configure', N'Cấu hình khu vực & vị trí đỗ'),
    (11, N'Pricing.Manage', N'Cấu hình bảng giá gửi xe'),
    (12, N'Report.View', N'Xem báo cáo doanh thu & lượt xe'),
    (13, N'Report.Export', N'Xuất báo cáo dữ liệu'),
    (14, N'Shift.View', N'Xem ca trực và lịch sử đối soát'),
    (15, N'Shift.Open', N'Mở ca trực'),
    (16, N'Shift.Close', N'Đóng ca trực'),
    (17, N'Shift.Review', N'Quản lý xác nhận ca'),
    (18, N'Shift.Adjust', N'Điều chỉnh ca đã khóa'),
    (19, N'Audit.View', N'Xem nhật ký hệ thống'),
    (20, N'Audit.Verify', N'Kiểm tra toàn vẹn nhật ký'),
    (21, N'Payment.Refund', N'Hoàn tiền / huỷ thanh toán'),
    (22, N'Settings.Manage', N'Quản lý cài đặt hệ thống'),
    (23, N'Incident.Manage', N'Xử lý sự cố')
) AS v(Ord, Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM [Permissions] p WHERE p.[PermissionName] = v.Name)
ORDER BY v.Ord;

-- -----------------------------------------------------------------------------------
-- 2. DANH MỤC VAI TRÒ (Roles) & 3. GÁN QUYỀN CHO VAI TRÒ (RolePermissions)
-- Quyền mặc định của Operator/Manager chỉ được gán khi vai trò vừa được tạo trong lần chạy này,
-- để các thay đổi trên màn hình Phân quyền không bị seed khôi phục lại ở lần khởi động sau.
-- -----------------------------------------------------------------------------------
DECLARE @NewRoles TABLE ([RoleId] int NOT NULL, [RoleName] nvarchar(50) NOT NULL);

INSERT INTO [Roles] ([RoleName], [Description])
OUTPUT inserted.[RoleId], inserted.[RoleName] INTO @NewRoles ([RoleId], [RoleName])
SELECT v.Name, v.Description
FROM (VALUES
    (1, N'Admin', N'Quản trị viên toàn quyền hệ thống bãi đỗ xe'),
    (2, N'Manager', N'Quản lý điều hành vận hành bãi đỗ xe'),
    (3, N'Operator', N'Nhân viên trực ca bốt soát vé bãi đỗ xe')
) AS v(Ord, Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM [Roles] r WHERE r.[RoleName] = v.Name)
ORDER BY v.Ord;

INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
SELECT nr.[RoleId], p.[PermissionId]
FROM @NewRoles nr
JOIN [Permissions] p
  ON p.[PermissionName] IN (N'Parking.View', N'Parking.CheckIn', N'Parking.CheckOut', N'Report.View', N'Shift.View', N'Shift.Open', N'Shift.Close')
WHERE nr.[RoleName] = N'Operator';

INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
SELECT nr.[RoleId], p.[PermissionId]
FROM @NewRoles nr
JOIN [Permissions] p
  ON (p.[PermissionName] IN (N'Report.View', N'Report.Export', N'Pricing.Manage', N'Parking.View', N'Parking.CheckIn', N'Parking.CheckOut',
                             N'Parking.Configure', N'Payment.Refund', N'User.View', N'Role.View', N'Audit.View', N'Incident.Manage')
      OR p.[PermissionName] LIKE N'Shift.%')
WHERE nr.[RoleName] = N'Manager';

-- Admin luôn có toàn bộ quyền (kể cả quyền mới được thêm sau này)
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
SELECT r.[RoleId], p.[PermissionId]
FROM [Roles] r
CROSS JOIN [Permissions] p
WHERE r.[RoleName] = N'Admin'
  AND NOT EXISTS (SELECT 1 FROM [RolePermissions] rp WHERE rp.[RoleId] = r.[RoleId] AND rp.[PermissionId] = p.[PermissionId]);

-- -----------------------------------------------------------------------------------
-- 4. TÀI KHOẢN QUẢN TRỊ VIÊN MẶC ĐỊNH (Users)
-- Username: admin
-- Password mặc định: Admin@123 (đã hash chuẩn BCrypt)
-- -----------------------------------------------------------------------------------
INSERT INTO [Users] ([Username], [PasswordHash], [FullName], [RoleId], [IsActive])
SELECT
    N'admin',
    N'$2a$11$4zQCT6o4m1i7fStbvC18teLVORe5LvV5BscyAQW/.QPh.bWeOKpgW',
    N'Quản trị viên Hệ thống',
    r.[RoleId],
    1
FROM [Roles] r
WHERE r.[RoleName] = N'Admin'
  AND NOT EXISTS (SELECT 1 FROM [Users] u WHERE u.[Username] = N'admin');

-- -----------------------------------------------------------------------------------
-- 5. LOẠI PHƯƠNG TIỆN (VehicleTypes)
-- -----------------------------------------------------------------------------------
INSERT INTO [VehicleTypes] ([TypeName], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    (1, N'Xe máy', N'Xe mô tô hai bánh, xe gắn máy'),
    (2, N'Xe ô tô', N'Xe ô tô con dưới 9 chỗ ngồi'),
    (3, N'Xe đạp / Xe điện', N'Xe đạp truyền thống, xe đạp điện, xe máy điện mini')
) AS v(Ord, Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM [VehicleTypes] t WHERE t.[TypeName] = v.Name)
ORDER BY v.Ord;

-- -----------------------------------------------------------------------------------
-- 6. BIỂU PHÍ TÍNH TIỀN (PricingRules)
-- Xe máy: 4h 5.000đ, ngày 25.000đ, tháng 1/3/6: 100.000/285.000/540.000đ
-- Xe ô tô: 4h 25.000đ, ngày 100.000đ, tháng 1/3/6: 1.200.000/3.400.000/6.500.000đ
-- Xe đạp / điện: 4h 2.000đ, ngày 10.000đ, tháng 1/3/6: 50.000/140.000/270.000đ
-- -----------------------------------------------------------------------------------
INSERT INTO [PricingRules] ([VehicleTypeId], [Block4hPrice], [DailyPrice], [Monthly1Price], [Monthly3Price], [Monthly6Price], [Description])
SELECT t.[VehicleTypeId], v.Block4h, v.Daily, v.M1, v.M3, v.M6, v.Description
FROM (VALUES
    (1, N'Xe máy', 5000.00, 25000.00, 100000.00, 285000.00, 540000.00, N'Bảng giá gửi xe máy tiêu chuẩn'),
    (2, N'Xe ô tô', 25000.00, 100000.00, 1200000.00, 3400000.00, 6500000.00, N'Bảng giá gửi xe ô tô tiêu chuẩn'),
    (3, N'Xe đạp / Xe điện', 2000.00, 10000.00, 50000.00, 140000.00, 270000.00, N'Bảng giá gửi xe đạp tiêu chuẩn')
) AS v(Ord, TypeName, Block4h, Daily, M1, M3, M6, Description)
JOIN [VehicleTypes] t ON t.[TypeName] = v.TypeName
WHERE NOT EXISTS (SELECT 1 FROM [PricingRules] pr WHERE pr.[VehicleTypeId] = t.[VehicleTypeId])
ORDER BY v.Ord;

-- -----------------------------------------------------------------------------------
-- 8. KHU VỰC BÃI XE (ParkingZones)
-- -----------------------------------------------------------------------------------
INSERT INTO [ParkingZones] ([ZoneCode], [ZoneName], [TotalCapacity], [Description], [VehicleTypeId])
SELECT v.Code, v.Name, v.Capacity, v.Description, t.[VehicleTypeId]
FROM (VALUES
    (1, N'ZONE_A', N'Khu A - Xe Máy (Tầng B1)', 20, N'Khu đỗ xe máy tầng hầm B1', N'Xe máy'),
    (2, N'ZONE_B', N'Khu B - Ô Tô (Tầng B2)', 10, N'Khu đỗ ô tô con tầng hầm B2', N'Xe ô tô')
) AS v(Ord, Code, Name, Capacity, Description, TypeName)
JOIN [VehicleTypes] t ON t.[TypeName] = v.TypeName
WHERE NOT EXISTS (SELECT 1 FROM [ParkingZones] z WHERE z.[ZoneCode] = v.Code)
ORDER BY v.Ord;

-- -----------------------------------------------------------------------------------
-- 9. CÁC VỊ TRÍ Ô ĐỖ MẪU (ParkingSlots)
-- Kèm tọa độ 2D Canvas để hiển thị giao diện sơ đồ bãi xe
-- -----------------------------------------------------------------------------------
-- Khu A: 10 ô đỗ xe máy A-01 đến A-10
INSERT INTO [ParkingSlots] ([SlotCode], [ZoneName], [ZoneId], [VehicleTypeId], [Status], [CoordX], [CoordY], [Width], [Height])
SELECT
    N'A-' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2),
    z.[ZoneName],
    z.[ZoneId],
    z.[VehicleTypeId],
    0, -- SlotStatus.Available
    40.0 + ((n.s - 1) % 5) * 90.0,
    40.0 + ((n.s - 1) / 5) * 140.0,
    70.0,
    110.0
FROM [ParkingZones] z
CROSS JOIN (VALUES (1), (2), (3), (4), (5), (6), (7), (8), (9), (10)) AS n(s)
WHERE z.[ZoneCode] = N'ZONE_A'
  AND NOT EXISTS (SELECT 1 FROM [ParkingSlots] x WHERE x.[SlotCode] = N'A-' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2))
ORDER BY n.s;

-- Khu B: 6 ô đỗ ô tô B-01 đến B-06
INSERT INTO [ParkingSlots] ([SlotCode], [ZoneName], [ZoneId], [VehicleTypeId], [Status], [CoordX], [CoordY], [Width], [Height])
SELECT
    N'B-' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2),
    z.[ZoneName],
    z.[ZoneId],
    z.[VehicleTypeId],
    0, -- SlotStatus.Available
    50.0 + ((n.s - 1) % 3) * 120.0,
    40.0 + ((n.s - 1) / 3) * 180.0,
    95.0,
    150.0
FROM [ParkingZones] z
CROSS JOIN (VALUES (1), (2), (3), (4), (5), (6)) AS n(s)
WHERE z.[ZoneCode] = N'ZONE_B'
  AND NOT EXISTS (SELECT 1 FROM [ParkingSlots] x WHERE x.[SlotCode] = N'B-' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2))
ORDER BY n.s;

-- -----------------------------------------------------------------------------------
-- 10. CÀI ĐẶT BÃI XE (ParkingSettings) - một dòng duy nhất SettingsId = 1
-- -----------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM [ParkingSettings] WHERE [SettingsId] = 1)
BEGIN
    SET IDENTITY_INSERT [ParkingSettings] ON;
    INSERT INTO [ParkingSettings] ([SettingsId], [DefaultMaxVehiclesPerHousehold]) VALUES (1, 2);
    SET IDENTITY_INSERT [ParkingSettings] OFF;
END;

-- -----------------------------------------------------------------------------------
-- 11. KHU DÀNH RIÊNG CHO CƯ DÂN (ZONE_R) - Audience: 0 = Mixed, 1 = ResidentOnly, 2 = VisitorOnly
-- ZONE_A và ZONE_B giữ mặc định Mixed.
-- -----------------------------------------------------------------------------------
INSERT INTO [ParkingZones] ([ZoneCode], [ZoneName], [TotalCapacity], [Description], [VehicleTypeId], [Audience])
SELECT N'ZONE_R', N'Khu Cư dân (B2)', 6, N'Khu dành riêng cho cư dân (xe máy và ô tô)', NULL, 1
WHERE NOT EXISTS (SELECT 1 FROM [ParkingZones] z WHERE z.[ZoneCode] = N'ZONE_R');

-- 4 ô xe máy R-M01 đến R-M04
INSERT INTO [ParkingSlots] ([SlotCode], [ZoneName], [ZoneId], [VehicleTypeId], [Status], [CoordX], [CoordY], [Width], [Height])
SELECT
    N'R-M' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2),
    z.[ZoneName],
    z.[ZoneId],
    t.[VehicleTypeId],
    0, -- SlotStatus.Available
    40.0 + ((n.s - 1) % 4) * 90.0,
    40.0,
    70.0,
    110.0
FROM [ParkingZones] z
JOIN [VehicleTypes] t ON t.[TypeName] = N'Xe máy'
CROSS JOIN (VALUES (1), (2), (3), (4)) AS n(s)
WHERE z.[ZoneCode] = N'ZONE_R'
  AND NOT EXISTS (SELECT 1 FROM [ParkingSlots] x WHERE x.[SlotCode] = N'R-M' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2))
ORDER BY n.s;

-- 2 ô ô tô R-C01 đến R-C02
INSERT INTO [ParkingSlots] ([SlotCode], [ZoneName], [ZoneId], [VehicleTypeId], [Status], [CoordX], [CoordY], [Width], [Height])
SELECT
    N'R-C' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2),
    z.[ZoneName],
    z.[ZoneId],
    t.[VehicleTypeId],
    0, -- SlotStatus.Available
    50.0 + ((n.s - 1) % 2) * 120.0,
    190.0,
    95.0,
    150.0
FROM [ParkingZones] z
JOIN [VehicleTypes] t ON t.[TypeName] = N'Xe ô tô'
CROSS JOIN (VALUES (1), (2)) AS n(s)
WHERE z.[ZoneCode] = N'ZONE_R'
  AND NOT EXISTS (SELECT 1 FROM [ParkingSlots] x WHERE x.[SlotCode] = N'R-C' + RIGHT(N'0' + CAST(n.s AS nvarchar(2)), 2))
ORDER BY n.s;

COMMIT TRANSACTION;
