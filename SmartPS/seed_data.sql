-- ===================================================================================
-- SMART PARKING SYSTEM (SmartPS) - SEED DATA SCRIPT (PostgreSQL)
-- File: seed_data.sql
-- Mô tả: Dữ liệu khởi tạo mặc định cho Phân quyền, Tài khoản Admin, Nghiệp vụ Bãi xe.
-- Cách chạy: 
--   1. PowerShell + Docker (sau khi chạy migrations):
--      Get-Content -Raw -Encoding UTF8 .\seed_data.sql | docker exec -i smartps-postgres psql -v ON_ERROR_STOP=1 -U smartps -d SmartPS
--   2. Hoặc mở trong pgAdmin / DBeaver / Navicat / DataGrip và Execute Script.
-- ===================================================================================

BEGIN;

-- -----------------------------------------------------------------------------------
-- 1. DANH MỤC QUYỀN HẠN (Permissions)
-- -----------------------------------------------------------------------------------
INSERT INTO "Permissions" ("PermissionName", "Description") VALUES
('User.View', 'Xem danh sách người dùng'),
('User.Create', 'Tạo người dùng mới'),
('User.Edit', 'Chỉnh sửa thông tin người dùng'),
('User.Delete', 'Xóa người dùng'),
('Role.View', 'Xem danh sách vai trò'),
('Role.Manage', 'Quản lý phân quyền vai trò'),
('Parking.View', 'Xem trạng thái bãi đỗ xe'),
('Parking.CheckIn', 'Soát vé xe vào'),
('Parking.CheckOut', 'Soát vé xe ra & tính phí'),
('Parking.Configure', 'Cấu hình khu vực & vị trí đỗ'),
('Pricing.Manage', 'Cấu hình bảng giá gửi xe'),
('Report.View', 'Xem báo cáo doanh thu & lượt xe'),
('Report.Export', 'Xuất báo cáo dữ liệu'),
('Shift.View', 'Xem ca trực và lịch sử đối soát'),
('Shift.Open', 'Mở ca trực'),
('Shift.Close', 'Đóng ca trực'),
('Shift.Review', 'Quản lý xác nhận ca'),
('Shift.Adjust', 'Điều chỉnh ca đã khóa'),
('Audit.View', 'Xem nhật ký hệ thống'),
('Audit.Verify', 'Kiểm tra toàn vẹn nhật ký'),
('Payment.Refund', 'Hoàn tiền / huỷ thanh toán'),
('Settings.Manage', 'Quản lý cài đặt hệ thống'),
('Incident.Manage', 'Xử lý sự cố')
ON CONFLICT ("PermissionName") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 2. DANH MỤC VAI TRÒ (Roles) & 3. GÁN QUYỀN CHO VAI TRÒ (RolePermissions)
-- Quyền mặc định của Operator/Manager chỉ được gán khi vai trò vừa được tạo trong lần chạy này,
-- để các thay đổi trên màn hình Phân quyền không bị seed khôi phục lại ở lần khởi động sau.
-- -----------------------------------------------------------------------------------
WITH new_roles AS (
    INSERT INTO "Roles" ("RoleName", "Description") VALUES
    ('Admin', 'Quản trị viên toàn quyền hệ thống bãi đỗ xe'),
    ('Manager', 'Quản lý điều hành vận hành bãi đỗ xe'),
    ('Operator', 'Nhân viên trực ca bốt soát vé bãi đỗ xe')
    ON CONFLICT ("RoleName") DO NOTHING
    RETURNING "RoleId", "RoleName"),
op AS (
    INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
    SELECT nr."RoleId", p."PermissionId"
    FROM new_roles nr
    JOIN "Permissions" p
      ON p."PermissionName" IN ('Parking.View', 'Parking.CheckIn', 'Parking.CheckOut', 'Report.View', 'Shift.View', 'Shift.Open', 'Shift.Close')
    WHERE nr."RoleName" = 'Operator'
    ON CONFLICT DO NOTHING
    RETURNING 1)
INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
SELECT nr."RoleId", p."PermissionId"
FROM new_roles nr
JOIN "Permissions" p
  ON (p."PermissionName" IN ('Report.View', 'Report.Export', 'Pricing.Manage', 'Parking.View', 'Parking.CheckIn', 'Parking.CheckOut',
                             'Parking.Configure', 'Payment.Refund', 'User.View', 'Role.View', 'Audit.View', 'Incident.Manage')
      OR p."PermissionName" LIKE 'Shift.%')
WHERE nr."RoleName" = 'Manager'
ON CONFLICT DO NOTHING;

-- Admin luôn có toàn bộ quyền (kể cả quyền mới được thêm sau này)
INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
SELECT r."RoleId", p."PermissionId"
FROM "Roles" r
CROSS JOIN "Permissions" p
WHERE r."RoleName" = 'Admin'
ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 4. TÀI KHOẢN QUẢN TRỊ VIÊN MẶC ĐỊNH (Users)
-- Username: admin
-- Password mặc định: Admin@123 (đã hash chuẩn BCrypt)
-- -----------------------------------------------------------------------------------
INSERT INTO "Users" ("Username", "PasswordHash", "FullName", "RoleId", "IsActive")
SELECT 
    'admin', 
    '$2a$11$4zQCT6o4m1i7fStbvC18teLVORe5LvV5BscyAQW/.QPh.bWeOKpgW', 
    'Quản trị viên Hệ thống', 
    r."RoleId", 
    true
FROM "Roles" r
WHERE r."RoleName" = 'Admin'
ON CONFLICT ("Username") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 5. LOẠI PHƯƠNG TIỆN (VehicleTypes)
-- -----------------------------------------------------------------------------------
INSERT INTO "VehicleTypes" ("TypeName", "Description") VALUES
('Xe máy', 'Xe mô tô hai bánh, xe gắn máy'),
('Xe ô tô', 'Xe ô tô con dưới 9 chỗ ngồi'),
('Xe đạp / Xe điện', 'Xe đạp truyền thống, xe đạp điện, xe máy điện mini')
ON CONFLICT ("TypeName") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 6. BIỂU PHÍ TÍNH TIỀN (PricingRules)
-- -----------------------------------------------------------------------------------
-- Biểu phí Xe máy: 4h 5.000đ, ngày 25.000đ, tháng 1/3/6: 100.000/285.000/540.000đ
INSERT INTO "PricingRules" ("VehicleTypeId", "Block4hPrice", "DailyPrice", "Monthly1Price", "Monthly3Price", "Monthly6Price", "Description")
SELECT v."VehicleTypeId", 5000.00, 25000.00, 100000.00, 285000.00, 540000.00, 'Bảng giá gửi xe máy tiêu chuẩn'
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe máy'
AND NOT EXISTS (SELECT 1 FROM "PricingRules" pr WHERE pr."VehicleTypeId" = v."VehicleTypeId");

-- Biểu phí Xe ô tô: 4h 25.000đ, ngày 100.000đ, tháng 1/3/6: 1.200.000/3.400.000/6.500.000đ
INSERT INTO "PricingRules" ("VehicleTypeId", "Block4hPrice", "DailyPrice", "Monthly1Price", "Monthly3Price", "Monthly6Price", "Description")
SELECT v."VehicleTypeId", 25000.00, 100000.00, 1200000.00, 3400000.00, 6500000.00, 'Bảng giá gửi xe ô tô tiêu chuẩn'
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe ô tô'
AND NOT EXISTS (SELECT 1 FROM "PricingRules" pr WHERE pr."VehicleTypeId" = v."VehicleTypeId");

-- Biểu phí Xe đạp / điện: 4h 2.000đ, ngày 10.000đ, tháng 1/3/6: 50.000/140.000/270.000đ
INSERT INTO "PricingRules" ("VehicleTypeId", "Block4hPrice", "DailyPrice", "Monthly1Price", "Monthly3Price", "Monthly6Price", "Description")
SELECT v."VehicleTypeId", 2000.00, 10000.00, 50000.00, 140000.00, 270000.00, 'Bảng giá gửi xe đạp tiêu chuẩn'
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe đạp / Xe điện'
AND NOT EXISTS (SELECT 1 FROM "PricingRules" pr WHERE pr."VehicleTypeId" = v."VehicleTypeId");

-- -----------------------------------------------------------------------------------
-- 8. KHU VỰC BÃI XE (ParkingZones)
-- -----------------------------------------------------------------------------------
INSERT INTO "ParkingZones" ("ZoneCode", "ZoneName", "TotalCapacity", "Description", "VehicleTypeId")
SELECT 'ZONE_A', 'Khu A - Xe Máy (Tầng B1)', 20, 'Khu đỗ xe máy tầng hầm B1', v."VehicleTypeId"
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe máy'
ON CONFLICT ("ZoneCode") DO NOTHING;

INSERT INTO "ParkingZones" ("ZoneCode", "ZoneName", "TotalCapacity", "Description", "VehicleTypeId")
SELECT 'ZONE_B', 'Khu B - Ô Tô (Tầng B2)', 10, 'Khu đỗ ô tô con tầng hầm B2', v."VehicleTypeId"
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe ô tô'
ON CONFLICT ("ZoneCode") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 9. CÁC VỊ TRÍ Ô ĐỖ MẪU (ParkingSlots)
-- Kèm tọa độ 2D Canvas để hiển thị giao diện sơ đồ bãi xe
-- -----------------------------------------------------------------------------------
-- Khu A: 10 ô đỗ xe máy A-01 đến A-10
INSERT INTO "ParkingSlots" ("SlotCode", "ZoneName", "ZoneId", "VehicleTypeId", "Status", "CoordX", "CoordY", "Width", "Height")
SELECT
    'A-' || LPAD(s::text, 2, '0'),
    z."ZoneName",
    z."ZoneId",
    z."VehicleTypeId",
    0, -- SlotStatus.Available
    40.0 + ((s - 1) % 5) * 90.0,
    40.0 + ((s - 1) / 5) * 140.0,
    70.0,
    110.0
FROM "ParkingZones" z, generate_series(1, 10) s
WHERE z."ZoneCode" = 'ZONE_A'
ON CONFLICT ("SlotCode") DO NOTHING;

-- Khu B: 6 ô đỗ ô tô B-01 đến B-06
INSERT INTO "ParkingSlots" ("SlotCode", "ZoneName", "ZoneId", "VehicleTypeId", "Status", "CoordX", "CoordY", "Width", "Height")
SELECT
    'B-' || LPAD(s::text, 2, '0'),
    z."ZoneName",
    z."ZoneId",
    z."VehicleTypeId",
    0, -- SlotStatus.Available
    50.0 + ((s - 1) % 3) * 120.0,
    40.0 + ((s - 1) / 3) * 180.0,
    95.0,
    150.0
FROM "ParkingZones" z, generate_series(1, 6) s
WHERE z."ZoneCode" = 'ZONE_B'
ON CONFLICT ("SlotCode") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 10. CÀI ĐẶT BÃI XE (ParkingSettings)
-- -----------------------------------------------------------------------------------
INSERT INTO "ParkingSettings" ("SettingsId", "DefaultMaxVehiclesPerHousehold") VALUES (1, 2)
ON CONFLICT ("SettingsId") DO NOTHING;

COMMIT;

