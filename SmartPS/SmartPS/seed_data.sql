-- ===================================================================================
-- SMART PARKING SYSTEM (SmartPS) - SEED DATA SCRIPT (PostgreSQL)
-- File: seed_data.sql
-- Mô tả: Dữ liệu khởi tạo mặc định cho Phân quyền, Tài khoản Admin, Nghiệp vụ Bãi xe.
-- Cách chạy: 
--   1. Qua Docker CLI: 
--      docker exec -i smartps-postgres psql -U smartps -d SmartPS < seed_data.sql
--   2. Hoặc mở trong pgAdmin / DBeaver / Navicat / DataGrip và Execute Script.
-- ===================================================================================

BEGIN;

-- -----------------------------------------------------------------------------------
-- 1. DANH MỤC QUYỀN HẠN (Permissions)
-- -----------------------------------------------------------------------------------
INSERT INTO "Permissions" ("PermissionName", "Description") VALUES
('UserView', 'Xem danh sách người dùng'),
('UserCreate', 'Tạo người dùng mới'),
('UserEdit', 'Chỉnh sửa thông tin người dùng'),
('UserDelete', 'Xóa người dùng'),
('RoleView', 'Xem danh sách vai trò'),
('RoleManage', 'Quản lý phân quyền vai trò'),
('ParkingView', 'Xem trạng thái bãi đỗ xe'),
('ParkingCheckIn', 'Soát vé xe vào'),
('ParkingCheckOut', 'Soát vé xe ra & tính phí'),
('ParkingConfigure', 'Cấu hình khu vực & vị trí đỗ'),
('PricingManage', 'Cấu hình bảng giá gửi xe'),
('ReportView', 'Xem báo cáo doanh thu & lượt xe'),
('ReportExport', 'Xuất báo cáo dữ liệu')
ON CONFLICT ("PermissionName") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 2. DANH MỤC VAI TRÒ (Roles)
-- -----------------------------------------------------------------------------------
INSERT INTO "Roles" ("RoleName", "Description") VALUES
('Admin', 'Quản trị viên toàn quyền hệ thống bãi đỗ xe'),
('Manager', 'Quản lý điều hành vận hành bãi đỗ xe'),
('Operator', 'Nhân viên trực ca bốt soát vé bãi đỗ xe')
ON CONFLICT ("RoleName") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 3. GÁN QUYỀN CHO VAI TRÒ (RolePermissions)
-- -----------------------------------------------------------------------------------
-- Gán toàn bộ quyền cho vai trò Admin
INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
SELECT r."RoleId", p."PermissionId"
FROM "Roles" r
CROSS JOIN "Permissions" p
WHERE r."RoleName" = 'Admin'
ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;

-- Gán quyền tác nghiệp cơ bản cho vai trò Operator (Xem bãi, check-in, check-out, xem báo cáo)
INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
SELECT r."RoleId", p."PermissionId"
FROM "Roles" r
JOIN "Permissions" p ON p."PermissionName" IN ('ParkingView', 'ParkingCheckIn', 'ParkingCheckOut', 'ReportView')
WHERE r."RoleName" = 'Operator'
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
-- Biểu phí Xe máy: 2h đầu 5.000đ, thêm 2.000đ/giờ, qua đêm 10.000đ
INSERT INTO "PricingRules" ("VehicleTypeId", "FirstBlockMinutes", "FirstBlockPrice", "AdditionalPricePerHour", "OvernightPrice", "Description")
SELECT v."VehicleTypeId", 120, 5000.00, 2000.00, 10000.00, 'Bảng giá gửi xe máy tiêu chuẩn'
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe máy'
AND NOT EXISTS (SELECT 1 FROM "PricingRules" pr WHERE pr."VehicleTypeId" = v."VehicleTypeId");

-- Biểu phí Xe ô tô: 2h đầu 25.000đ, thêm 15.000đ/giờ, qua đêm 50.000đ
INSERT INTO "PricingRules" ("VehicleTypeId", "FirstBlockMinutes", "FirstBlockPrice", "AdditionalPricePerHour", "OvernightPrice", "Description")
SELECT v."VehicleTypeId", 120, 25000.00, 15000.00, 50000.00, 'Bảng giá gửi xe ô tô tiêu chuẩn'
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe ô tô'
AND NOT EXISTS (SELECT 1 FROM "PricingRules" pr WHERE pr."VehicleTypeId" = v."VehicleTypeId");

-- Biểu phí Xe đạp / điện: 2h đầu 2.000đ, thêm 1.000đ/giờ, qua đêm 5.000đ
INSERT INTO "PricingRules" ("VehicleTypeId", "FirstBlockMinutes", "FirstBlockPrice", "AdditionalPricePerHour", "OvernightPrice", "Description")
SELECT v."VehicleTypeId", 120, 2000.00, 1000.00, 5000.00, 'Bảng giá gửi xe đạp tiêu chuẩn'
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe đạp / Xe điện'
AND NOT EXISTS (SELECT 1 FROM "PricingRules" pr WHERE pr."VehicleTypeId" = v."VehicleTypeId");

-- -----------------------------------------------------------------------------------
-- 7. HẠNG KHÁCH HÀNG (CustomerTiers)
-- -----------------------------------------------------------------------------------
INSERT INTO "CustomerTiers" ("CustomerType", "TierName", "DiscountPercentage", "BadgeColor", "BadgeIcon", "Description", "IsActive") VALUES
(0, 'Khách Vãng Lai', 0.0, '#64748B', '👤', 'Khách vãng lai, gửi xe theo lượt tiêu chuẩn', true),
(1, 'Khách Thân Quen', 10.0, '#3B82F6', '🌟', 'Khách hàng thường xuyên, giảm 10% vé tháng và lượt', true),
(2, 'Khách VIP / Cư Dân', 20.0, '#F59E0B', '👑', 'Cư dân căn hộ / Khách VIP, giảm 20% và ưu tiên vị trí đỗ', true)
ON CONFLICT ("CustomerType") DO NOTHING;

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
-- 10. GÓI VÉ THÁNG MẪU (MonthlyTicketPlans)
-- -----------------------------------------------------------------------------------
INSERT INTO "MonthlyTicketPlans" ("PlanName", "VehicleTypeId", "DurationMonths", "PricePerMonth", "DiscountPercentage", "TotalPrice", "Description", "IsActive")
SELECT 'Gói Xe Máy 1 Tháng', v."VehicleTypeId", 1, 120000.00, 0.0, 120000.00, 'Vé gửi xe máy định kỳ 1 tháng', true
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe máy'
AND NOT EXISTS (SELECT 1 FROM "MonthlyTicketPlans" p WHERE p."PlanName" = 'Gói Xe Máy 1 Tháng');

INSERT INTO "MonthlyTicketPlans" ("PlanName", "VehicleTypeId", "DurationMonths", "PricePerMonth", "DiscountPercentage", "TotalPrice", "Description", "IsActive")
SELECT 'Gói Xe Máy 3 Tháng (Tiết kiệm)', v."VehicleTypeId", 3, 120000.00, 5.5, 340000.00, 'Vé gửi xe máy 3 tháng giảm giá đặc biệt', true
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe máy'
AND NOT EXISTS (SELECT 1 FROM "MonthlyTicketPlans" p WHERE p."PlanName" = 'Gói Xe Máy 3 Tháng (Tiết kiệm)');

INSERT INTO "MonthlyTicketPlans" ("PlanName", "VehicleTypeId", "DurationMonths", "PricePerMonth", "DiscountPercentage", "TotalPrice", "Description", "IsActive")
SELECT 'Gói Ô Tô 1 Tháng', v."VehicleTypeId", 1, 1200000.00, 0.0, 1200000.00, 'Vé gửi ô tô định kỳ 1 tháng', true
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe ô tô'
AND NOT EXISTS (SELECT 1 FROM "MonthlyTicketPlans" p WHERE p."PlanName" = 'Gói Ô Tô 1 Tháng');

INSERT INTO "MonthlyTicketPlans" ("PlanName", "VehicleTypeId", "DurationMonths", "PricePerMonth", "DiscountPercentage", "TotalPrice", "Description", "IsActive")
SELECT 'Gói Ô Tô 3 Tháng (Tiết kiệm)', v."VehicleTypeId", 3, 1200000.00, 5.5, 3400000.00, 'Vé gửi ô tô 3 tháng tiết kiệm chi phí', true
FROM "VehicleTypes" v WHERE v."TypeName" = 'Xe ô tô'
AND NOT EXISTS (SELECT 1 FROM "MonthlyTicketPlans" p WHERE p."PlanName" = 'Gói Ô Tô 3 Tháng (Tiết kiệm)');

COMMIT;

