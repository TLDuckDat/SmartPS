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
('Incident.Manage', 'Xử lý sự cố'),
('Customer.View', 'Xem khách hàng, vé tháng và danh sách đen'),
('Customer.Manage', 'Quản lý khách hàng, phương tiện và vé tháng'),
('Blacklist.Manage', 'Quản lý danh sách đen biển số')
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
      ON p."PermissionName" IN ('Parking.View', 'Parking.CheckIn', 'Parking.CheckOut', 'Report.View', 'Shift.View', 'Shift.Open', 'Shift.Close', 'Customer.View')
    WHERE nr."RoleName" = 'Operator'
    ON CONFLICT DO NOTHING
    RETURNING 1)
INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
SELECT nr."RoleId", p."PermissionId"
FROM new_roles nr
JOIN "Permissions" p
  ON (p."PermissionName" IN ('Report.View', 'Report.Export', 'Pricing.Manage', 'Parking.View', 'Parking.CheckIn', 'Parking.CheckOut',
                             'Parking.Configure', 'Payment.Refund', 'User.View', 'Role.View', 'Audit.View', 'Incident.Manage',
                             'Customer.View', 'Customer.Manage', 'Blacklist.Manage')
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
(2, 'Khách VIP', 20.0, '#F59E0B', '👑', 'Khách VIP, giảm 20% và ưu tiên vị trí đỗ', true),
(3, 'Cư Dân', 0.0, '#16A34A', '🏠', 'Cư dân toà nhà có vé tháng còn hạn', true)
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

-- -----------------------------------------------------------------------------------
-- 11. KHU DÀNH RIÊNG CHO CƯ DÂN (ZONE_R) - Audience: 0 = Mixed, 1 = ResidentOnly, 2 = VisitorOnly
-- ZONE_A và ZONE_B giữ mặc định Mixed.
-- -----------------------------------------------------------------------------------
INSERT INTO "ParkingZones" ("ZoneCode", "ZoneName", "TotalCapacity", "Description", "VehicleTypeId", "Audience")
VALUES ('ZONE_R', 'Khu Cư dân (B2)', 6, 'Khu dành riêng cho cư dân (xe máy và ô tô)', NULL, 1)
ON CONFLICT ("ZoneCode") DO NOTHING;

-- 4 ô xe máy R-M01 đến R-M04
INSERT INTO "ParkingSlots" ("SlotCode", "ZoneName", "ZoneId", "VehicleTypeId", "Status", "CoordX", "CoordY", "Width", "Height")
SELECT
    'R-M' || LPAD(s::text, 2, '0'),
    z."ZoneName",
    z."ZoneId",
    v."VehicleTypeId",
    0, -- SlotStatus.Available
    40.0 + ((s - 1) % 4) * 90.0,
    40.0,
    70.0,
    110.0
FROM "ParkingZones" z
JOIN "VehicleTypes" v ON v."TypeName" = 'Xe máy'
CROSS JOIN generate_series(1, 4) s
WHERE z."ZoneCode" = 'ZONE_R'
ON CONFLICT ("SlotCode") DO NOTHING;

-- 2 ô ô tô R-C01 đến R-C02
INSERT INTO "ParkingSlots" ("SlotCode", "ZoneName", "ZoneId", "VehicleTypeId", "Status", "CoordX", "CoordY", "Width", "Height")
SELECT
    'R-C' || LPAD(s::text, 2, '0'),
    z."ZoneName",
    z."ZoneId",
    v."VehicleTypeId",
    0, -- SlotStatus.Available
    50.0 + ((s - 1) % 2) * 120.0,
    190.0,
    95.0,
    150.0
FROM "ParkingZones" z
JOIN "VehicleTypes" v ON v."TypeName" = 'Xe ô tô'
CROSS JOIN generate_series(1, 2) s
WHERE z."ZoneCode" = 'ZONE_R'
ON CONFLICT ("SlotCode") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 12. KHÁCH HÀNG MẪU (Customers): 5 cư dân + 1 khách thuê bao không phải cư dân
-- Type: 3 = Resident, 0 = Regular. Khoá chống trùng: số điện thoại.
-- -----------------------------------------------------------------------------------
INSERT INTO "Customers" ("FullName", "PhoneNumber", "DefaultLicensePlate", "Type", "VehicleTypeId", "CreatedAt", "IsActive", "Notes", "IsResident", "ApartmentCode", "Building")
SELECT d.full_name, d.phone, d.plate, d.ctype, v."VehicleTypeId", now(), true, 'Dữ liệu mẫu', d.is_resident, d.apartment, d.building
FROM (VALUES
    ('Nguyễn Văn Hùng',    '0988123456', '51F12345',  'Xe ô tô', 3, true,  'A-1205', 'A'),
    ('Trần Thị Mai Hương', '0912888999', '29B188888', 'Xe máy',  3, true,  'A-0803', 'A'),
    ('Lê Hoàng Long',      '0977345678', '30F99999',  'Xe ô tô', 3, true,  'B-1510', 'B'),
    ('Phạm Quốc Tuấn',     '0904567890', '29D212345', 'Xe máy',  3, true,  'B-0402', 'B'),
    ('Đặng Thùy Dung',     '0936789012', '30A67890',  'Xe ô tô', 3, true,  'C-2101', 'C'),
    ('Võ Minh Khang',      '0911222333', '59X312345', 'Xe máy',  0, false, NULL,     NULL)
) AS d(full_name, phone, plate, vehicle_type, ctype, is_resident, apartment, building)
JOIN "VehicleTypes" v ON v."TypeName" = d.vehicle_type
WHERE NOT EXISTS (SELECT 1 FROM "Customers" c WHERE c."PhoneNumber" = d.phone);

-- -----------------------------------------------------------------------------------
-- 13. PHƯƠNG TIỆN CỦA KHÁCH (CustomerVehicles)
-- Biển số đã từng xuất hiện (kể cả đã gỡ) sẽ không được thêm lại.
-- -----------------------------------------------------------------------------------
INSERT INTO "CustomerVehicles" ("CustomerId", "LicensePlate", "VehicleTypeId", "IsActive", "CreatedAt")
SELECT c."CustomerId", d.plate, v."VehicleTypeId", true, now()
FROM (VALUES
    ('0988123456', '51F12345',  'Xe ô tô'),
    ('0988123456', '59T112345', 'Xe máy'),
    ('0912888999', '29B188888', 'Xe máy'),
    ('0977345678', '30F99999',  'Xe ô tô'),
    ('0977345678', '29H155555', 'Xe máy'),
    ('0904567890', '29D212345', 'Xe máy'),
    ('0936789012', '30A67890',  'Xe ô tô'),
    ('0911222333', '59X312345', 'Xe máy')
) AS d(phone, plate, vehicle_type)
JOIN "Customers" c ON c."PhoneNumber" = d.phone
JOIN "VehicleTypes" v ON v."TypeName" = d.vehicle_type
WHERE NOT EXISTS (SELECT 1 FROM "CustomerVehicles" cv WHERE cv."LicensePlate" = d.plate);

-- -----------------------------------------------------------------------------------
-- 14. VÉ THÁNG MẪU (MonthlyTickets)
-- EndDate = 00:00 giờ VN của (hôm nay + end_offset ngày); StartDate = EndDate - số tháng của gói.
-- -----------------------------------------------------------------------------------
INSERT INTO "MonthlyTickets" ("TicketCode", "CustomerId", "RegisteredLicensePlate", "PlanId", "VehicleTypeId", "StartDate", "EndDate", "MonthlyPrice", "Status", "Notes", "CreatedAt")
SELECT d.code, c."CustomerId", d.plate, p."PlanId", p."VehicleTypeId",
       ((vn.today_vn + make_interval(days => d.end_offset))::timestamp - make_interval(months => p."DurationMonths") - interval '7 hours') AT TIME ZONE 'UTC',
       ((vn.today_vn + make_interval(days => d.end_offset))::timestamp - interval '7 hours') AT TIME ZONE 'UTC',
       p."TotalPrice", 0, 'Dữ liệu mẫu', now()
FROM (SELECT ((now() AT TIME ZONE 'UTC') + interval '7 hours')::date AS today_vn) vn
CROSS JOIN (VALUES
    ('MT-SEED-001', '0988123456', '51F12345',  'Gói Ô Tô 3 Tháng (Tiết kiệm)',  60),
    ('MT-SEED-002', '0912888999', '29B188888', 'Gói Xe Máy 1 Tháng',             3),
    ('MT-SEED-003', '0977345678', '30F99999',  'Gói Ô Tô 1 Tháng',               -5),
    ('MT-SEED-004', '0904567890', '29D212345', 'Gói Xe Máy 3 Tháng (Tiết kiệm)', 75),
    ('MT-SEED-005', '0936789012', '30A67890',  'Gói Ô Tô 1 Tháng',               20),
    ('MT-SEED-006', '0911222333', '59X312345', 'Gói Xe Máy 1 Tháng',             25)
) AS d(code, phone, plate, plan_name, end_offset)
JOIN "Customers" c ON c."PhoneNumber" = d.phone
JOIN "MonthlyTicketPlans" p ON p."PlanName" = d.plan_name
ON CONFLICT ("TicketCode") DO NOTHING;

-- -----------------------------------------------------------------------------------
-- 15. LỊCH SỬ MUA VÉ THÁNG (MonthlyTicketPurchases): mỗi vé mẫu có đúng một dòng mua mới (Kind = 0)
-- -----------------------------------------------------------------------------------
INSERT INTO "MonthlyTicketPurchases" ("TicketId", "Kind", "PlanId", "Price", "PeriodStartUtc", "PeriodEndUtc", "CreatedAtUtc", "CreatedByUserId")
SELECT t."TicketId", 0, t."PlanId", t."MonthlyPrice", t."StartDate", t."EndDate", t."StartDate", u."UserId"
FROM "MonthlyTickets" t
JOIN "Users" u ON u."Username" = 'admin'
WHERE t."TicketCode" LIKE 'MT-SEED-%'
  AND NOT EXISTS (SELECT 1 FROM "MonthlyTicketPurchases" p WHERE p."TicketId" = t."TicketId" AND p."Kind" = 0);

-- -----------------------------------------------------------------------------------
-- 16. DANH SÁCH ĐEN MẪU (BlacklistEntries)
-- Biển số đã từng xuất hiện (kể cả đã gỡ) sẽ không được thêm lại.
-- -----------------------------------------------------------------------------------
INSERT INTO "BlacklistEntries" ("LicensePlate", "Reason", "CreatedAt", "CreatedByUserId", "IsActive")
SELECT d.plate, d.reason, now(), u."UserId", true
FROM (VALUES
    ('29A99999', 'Nợ phí gửi xe nhiều lần, chưa thanh toán'),
    ('30G11111', 'Xe được báo mất cắp - liên hệ công an phường')
) AS d(plate, reason)
JOIN "Users" u ON u."Username" = 'admin'
WHERE NOT EXISTS (SELECT 1 FROM "BlacklistEntries" b WHERE b."LicensePlate" = d.plate);

COMMIT;

