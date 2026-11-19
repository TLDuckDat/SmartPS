-- ===================================================================================
-- SMARTPS - DỮ LIỆU MẪU ĐỂ KIỂM THỬ (SQL Server / T-SQL)
-- File: sample_data.sql
--
-- Chạy SAU khi ứng dụng đã khởi tạo database (migrations + seed_data.sql).
-- Chạy lại nhiều lần được: phần đầu script xoá dữ liệu mẫu cũ rồi sinh lại. Dữ liệu mẫu được nhận diện bằng
-- ghi chú [MẪU] (hộ, khách hàng, danh sách cấm, sự cố, thanh toán) và 7 tài khoản mẫu (ca trực, giao dịch,
-- lượt gửi do các tài khoản này tạo). Dữ liệu do tài khoản mẫu tạo khi bạn test trên giao diện cũng bị xoá.
-- Mã chứng từ theo đúng định dạng của ứng dụng: VL-yyMMdd-0001 (vé lượt), GD-yyMMdd-0001 (giao dịch),
-- CA-yyMMdd-01 (ca trực), VT-yyyy-0001 (vé tháng); số thứ tự nối tiếp các mã đã có trong database.
-- KHÔNG ghi vào bảng AuditLogs (chuỗi băm) nên "Kiểm tra nhật ký" vẫn hợp lệ.
--
-- Nội dung sinh ra:
--   * 7 tài khoản: 4 nhân viên + 2 quản lý + 1 tài khoản đã khoá (mật khẩu giống tài khoản admin)
--   * 3 khu đỗ mới: ZONE_C (xe máy, chỉ khách vãng lai, 30 ô, 2 ô bảo trì), ZONE_D (ô tô, dùng chung, 12 ô),
--     ZONE_E (xe đạp/điện, dùng chung, 15 ô)
--   * ~60 hộ dân (2 hộ ngưng hoạt động, hộ giới hạn 1 xe, hộ giới hạn 3 xe), ~100 cư dân, 45 khách vé tháng
--   * ~150 xe; nhiều hộ có xe vượt định mức
--   * ~70 vé tháng đủ trạng thái: còn hạn, sắp hết hạn (<15 ngày), hết hạn, tạm khoá, đã huỷ,
--     loại xe không khớp, chủ xe bị khoá; kèm lịch sử cấp / gia hạn / đổi biển / khoá / huỷ
--   * Danh sách cấm: đang cấm, đã gỡ cấm, biển số viết không chuẩn
--   * 60 ngày lịch sử: 3 ca/ngày (06-14, 14-22, 22-06 giờ VN), ~7.000 lượt gửi đã hoàn thành,
--     tiền mặt / VietQR / miễn phí, thanh toán QR bị huỷ rồi trả lại, hoàn tiền, điều chỉnh, phí sự cố,
--     ca chênh lệch tiền, ca đã khoá chờ duyệt, ca đã duyệt; 1 ca đang mở của nv.an
--   * ~45 xe đang trong bãi: cư dân, vé tháng, vãng lai, gửi quá hạn (>48h), vé tháng hết hạn khi xe
--     đang trong bãi, xe vượt định mức, 1 thanh toán VietQR đang chờ trong ca đang mở
--   * ~24 biên bản sự cố đủ loại, đang xử lý và đã giải quyết
--
-- Cách chạy (PowerShell / CMD), sửa -S cho đúng máy chủ trong appsettings:
--   sqlcmd -S "ADMIN-PC\SQLEXPRESS" -d SmartPS -E -C -f 65001 -i SmartPS\sample_data.sql
--   sqlcmd -S "(localdb)\MSSQLLocalDB" -d SmartPS -E -C -f 65001 -i SmartPS\sample_data.sql
-- Hoặc mở file trong SSMS, chọn database SmartPS và bấm Execute (F5).
-- ===================================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Bắt buộc cho bảng có chỉ mục có điều kiện (sqlcmd mặc định tắt QUOTED_IDENTIFIER)
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;

BEGIN TRANSACTION;

DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @VnToday date = CAST(DATEADD(HOUR, 7, @Now) AS date);
DECLARE @Mark nvarchar(10) = N'[MẪU]';

-- ===================================================================================
-- 0. DỌN DỮ LIỆU MẪU CŨ
-- ===================================================================================
DECLARE @SampleUsers TABLE (Username nvarchar(50) PRIMARY KEY);
INSERT INTO @SampleUsers VALUES (N'nv.an'), (N'nv.binh'), (N'nv.cuong'), (N'nv.dung'), (N'ql.hoa'), (N'ql.khanh'), (N'nv.nghiviec');

IF COL_LENGTH('dbo.Shifts', 'ShiftCode') IS NULL
    THROW 50002, N'Database chưa có cột Shifts.ShiftCode. Hãy chạy phiên bản mới của ứng dụng một lần để áp dụng migration.', 1;

DECLARE @SampleUserIds TABLE (UserId int PRIMARY KEY);
INSERT INTO @SampleUserIds SELECT u.UserId FROM Users u JOIN @SampleUsers su ON su.Username = u.Username;
DECLARE @SampleCustomers TABLE (CustomerId int PRIMARY KEY);
INSERT INTO @SampleCustomers SELECT CustomerId FROM Customers WHERE Notes LIKE N'[[]MẪU]%';
DECLARE @SampleShifts TABLE (ShiftId int PRIMARY KEY);
INSERT INTO @SampleShifts SELECT ShiftId FROM Shifts WHERE OpenedByUserId IN (SELECT UserId FROM @SampleUserIds);

DECLARE @OldSessions TABLE (SessionId int PRIMARY KEY);
INSERT INTO @OldSessions
SELECT SessionId FROM ParkingSessions
WHERE CreatedByUserId IN (SELECT UserId FROM @SampleUserIds)
   OR CustomerId IN (SELECT CustomerId FROM @SampleCustomers)
   OR TicketCode LIKE N'TK-SP-%' OR TicketCode LIKE N'MT-SP-%'   -- dữ liệu mẫu phiên bản cũ
   OR SessionId IN (SELECT ft.ParkingSessionId FROM FinancialTransactions ft WHERE ft.ShiftId IN (SELECT ShiftId FROM @SampleShifts));

DECLARE @OldPayments TABLE (PaymentId int PRIMARY KEY);
INSERT INTO @OldPayments
SELECT PaymentId FROM Payments
WHERE Description LIKE N'[[]MẪU]%' OR SessionId IN (SELECT SessionId FROM @OldSessions) OR ShiftId IN (SELECT ShiftId FROM @SampleShifts);

DELETE FROM PaymentWebhooks WHERE PaymentTransactionId IN (SELECT PaymentTransactionId FROM PaymentTransactions WHERE PaymentId IN (SELECT PaymentId FROM @OldPayments));
DELETE FROM PaymentAttempts WHERE PaymentTransactionId IN (SELECT PaymentTransactionId FROM PaymentTransactions WHERE PaymentId IN (SELECT PaymentId FROM @OldPayments));
DELETE FROM PaymentTransactions WHERE PaymentId IN (SELECT PaymentId FROM @OldPayments);
DELETE FROM Payments WHERE PaymentId IN (SELECT PaymentId FROM @OldPayments);
DELETE FROM FinancialTransactions
WHERE TransactionCode LIKE N'TX-SP-%' OR ParkingSessionId IN (SELECT SessionId FROM @OldSessions) OR ShiftId IN (SELECT ShiftId FROM @SampleShifts);

UPDATE s SET s.Status = 0, s.CurrentLicensePlate = NULL
FROM ParkingSlots s
WHERE s.SlotId IN (SELECT ps.SlotId FROM ParkingSessions ps JOIN @OldSessions o ON o.SessionId = ps.SessionId WHERE ps.Status = 0 AND ps.SlotId IS NOT NULL);
DELETE FROM ParkingSessions WHERE SessionId IN (SELECT SessionId FROM @OldSessions);

-- Ca trực của tài khoản mẫu (giao dịch và thanh toán trong ca đã xoá ở trên)
DELETE FROM Shifts WHERE ShiftId IN (SELECT ShiftId FROM @SampleShifts);

DELETE FROM Incidents WHERE Description LIKE N'[[]MẪU]%';

DECLARE @OldCustomers TABLE (CustomerId int PRIMARY KEY);
INSERT INTO @OldCustomers SELECT CustomerId FROM Customers WHERE Notes LIKE N'[[]MẪU]%';

UPDATE ParkingSessions SET CustomerId = NULL WHERE CustomerId IN (SELECT CustomerId FROM @OldCustomers);
DELETE FROM MonthlyTicketHistories WHERE TicketId IN (
    SELECT TicketId FROM MonthlyTickets WHERE TicketCode LIKE N'MT-SP-%' OR CustomerId IN (SELECT CustomerId FROM @OldCustomers));
DELETE FROM MonthlyTickets WHERE TicketCode LIKE N'MT-SP-%' OR CustomerId IN (SELECT CustomerId FROM @OldCustomers);
DELETE FROM BlacklistedVehicles WHERE Notes LIKE N'[[]MẪU]%' OR CustomerId IN (SELECT CustomerId FROM @OldCustomers);
DELETE FROM Vehicles WHERE OwnerCustomerId IN (SELECT CustomerId FROM @OldCustomers);
DELETE FROM Customers WHERE CustomerId IN (SELECT CustomerId FROM @OldCustomers);
DELETE FROM Households WHERE Notes LIKE N'[[]MẪU]%' AND NOT EXISTS (SELECT 1 FROM Customers c WHERE c.HouseholdId = Households.HouseholdId);

-- ===================================================================================
-- 1. TÀI KHOẢN (mật khẩu = mật khẩu hiện tại của admin, mặc định Admin@123)
-- ===================================================================================
DECLARE @Hash nvarchar(255) = (SELECT TOP 1 PasswordHash FROM Users WHERE Username = N'admin');
IF @Hash IS NULL
    THROW 50001, N'Không tìm thấy tài khoản admin. Hãy chạy ứng dụng một lần để khởi tạo database trước.', 1;

INSERT INTO Users (Username, PasswordHash, FullName, PhoneNumber, Email, IsActive, RoleId)
SELECT v.Username, @Hash, v.FullName, v.Phone, v.Email, v.Active, r.RoleId
FROM (VALUES
    (N'nv.an',       N'Nguyễn Văn An',      N'0901000001', N'nv.an@sample.smartps',    1, N'Operator'),
    (N'nv.binh',     N'Trần Thị Bình',      N'0901000002', N'nv.binh@sample.smartps',  1, N'Operator'),
    (N'nv.cuong',    N'Lê Minh Cường',      N'0901000003', N'nv.cuong@sample.smartps', 1, N'Operator'),
    (N'nv.dung',     N'Phạm Quốc Dũng',     N'0901000004', N'nv.dung@sample.smartps',  1, N'Operator'),
    (N'ql.hoa',      N'Võ Thị Hoa',         N'0902000001', N'ql.hoa@sample.smartps',   1, N'Manager'),
    (N'ql.khanh',    N'Đặng Gia Khánh',     N'0902000002', N'ql.khanh@sample.smartps', 1, N'Manager'),
    (N'nv.nghiviec', N'Bùi Văn Nghỉ (đã khoá)', N'0901000009', N'nv.nghiviec@sample.smartps', 0, N'Operator')
) AS v(Username, FullName, Phone, Email, Active, RoleName)
JOIN Roles r ON r.RoleName = v.RoleName
WHERE NOT EXISTS (SELECT 1 FROM Users u WHERE u.Username = v.Username);

DECLARE @Ops TABLE (Idx int PRIMARY KEY, UserId int);
INSERT INTO @Ops SELECT ROW_NUMBER() OVER (ORDER BY Username) - 1, UserId FROM Users WHERE Username IN (N'nv.an', N'nv.binh', N'nv.cuong', N'nv.dung');
DECLARE @OpAn int = (SELECT UserId FROM Users WHERE Username = N'nv.an');
DECLARE @MgrHoa int = (SELECT UserId FROM Users WHERE Username = N'ql.hoa');
DECLARE @MgrKhanh int = (SELECT UserId FROM Users WHERE Username = N'ql.khanh');

-- ===================================================================================
-- 2. KHU ĐỖ VÀ Ô ĐỖ BỔ SUNG
-- ===================================================================================
DECLARE @TMoto int = (SELECT VehicleTypeId FROM VehicleTypes WHERE TypeName = N'Xe máy');
DECLARE @TCar  int = (SELECT VehicleTypeId FROM VehicleTypes WHERE TypeName = N'Xe ô tô');
DECLARE @TBike int = (SELECT VehicleTypeId FROM VehicleTypes WHERE TypeName = N'Xe đạp / Xe điện');

INSERT INTO ParkingZones (ZoneCode, ZoneName, TotalCapacity, Description, VehicleTypeId, Audience)
SELECT v.Code, v.Name, v.Cap, v.Descr, v.TypeId, v.Aud
FROM (VALUES
    (N'ZONE_C', N'Khu C - Xe máy khách (Tầng B1)', 30, N'Khu xe máy chỉ dành cho khách vãng lai', @TMoto, 2),
    (N'ZONE_D', N'Khu D - Ô tô (Tầng B3)',          12, N'Khu ô tô dùng chung',                    @TCar,  0),
    (N'ZONE_E', N'Khu E - Xe đạp, xe điện (Sân sau)', 15, N'Khu xe đạp, xe điện dùng chung',        @TBike, 0)
) AS v(Code, Name, Cap, Descr, TypeId, Aud)
WHERE NOT EXISTS (SELECT 1 FROM ParkingZones z WHERE z.ZoneCode = v.Code);

;WITH N AS (SELECT TOP (30) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
INSERT INTO ParkingSlots (SlotCode, ZoneName, ZoneId, VehicleTypeId, Status, CoordX, CoordY, Width, Height)
SELECT x.Code, z.ZoneName, z.ZoneId, z.VehicleTypeId, 0,
       40.0 + ((N.n - 1) % x.PerRow) * x.StepX, 40.0 + ((N.n - 1) / x.PerRow) * x.StepY, x.W, x.H
FROM N
CROSS APPLY (VALUES
    (N'ZONE_C', N'C-' + RIGHT(N'0' + CAST(N.n AS nvarchar(3)), 2), 30, 10, 90.0, 140.0, 70.0, 110.0),
    (N'ZONE_D', N'D-' + RIGHT(N'0' + CAST(N.n AS nvarchar(3)), 2), 12,  6, 120.0, 190.0, 95.0, 150.0),
    (N'ZONE_E', N'E-' + RIGHT(N'0' + CAST(N.n AS nvarchar(3)), 2), 15,  5, 90.0, 130.0, 60.0, 100.0)
) AS x(ZoneCode, Code, Cnt, PerRow, StepX, StepY, W, H)
JOIN ParkingZones z ON z.ZoneCode = x.ZoneCode
WHERE N.n <= x.Cnt
  AND NOT EXISTS (SELECT 1 FROM ParkingSlots s WHERE s.SlotCode = x.Code);

-- 2 ô đang bảo trì
UPDATE ParkingSlots SET Status = 2, CurrentLicensePlate = NULL
WHERE SlotCode IN (N'C-29', N'C-30')
  AND NOT EXISTS (SELECT 1 FROM ParkingSessions ps WHERE ps.SlotId = ParkingSlots.SlotId AND ps.Status = 0);

-- ===================================================================================
-- 3. DANH SÁCH TÊN, HÃNG XE, MÀU
-- ===================================================================================
DECLARE @Ho TABLE (i int PRIMARY KEY, v nvarchar(20));
INSERT INTO @Ho VALUES (0,N'Nguyễn'),(1,N'Trần'),(2,N'Lê'),(3,N'Phạm'),(4,N'Hoàng'),(5,N'Huỳnh'),(6,N'Phan'),
    (7,N'Vũ'),(8,N'Võ'),(9,N'Đặng'),(10,N'Bùi'),(11,N'Đỗ'),(12,N'Hồ'),(13,N'Ngô');
DECLARE @Dem TABLE (i int PRIMARY KEY, v nvarchar(20));
INSERT INTO @Dem VALUES (0,N'Văn'),(1,N'Thị'),(2,N'Minh'),(3,N'Thanh'),(4,N'Quốc'),(5,N'Ngọc'),(6,N'Hoàng'),
    (7,N'Thu'),(8,N'Đức'),(9,N'Gia');
DECLARE @Ten TABLE (i int PRIMARY KEY, v nvarchar(20));
INSERT INTO @Ten VALUES (0,N'An'),(1,N'Bảo'),(2,N'Châu'),(3,N'Dũng'),(4,N'Giang'),(5,N'Hà'),(6,N'Hải'),(7,N'Hạnh'),
    (8,N'Hiếu'),(9,N'Hùng'),(10,N'Khoa'),(11,N'Lan'),(12,N'Linh'),(13,N'Long'),(14,N'Mai'),(15,N'Nam'),(16,N'Nhung'),
    (17,N'Phong'),(18,N'Phúc'),(19,N'Quân'),(20,N'Quỳnh'),(21,N'Sơn'),(22,N'Tâm'),(23,N'Thảo'),(24,N'Thắng'),
    (25,N'Trang'),(26,N'Trí'),(27,N'Tuấn'),(28,N'Vy'),(29,N'Yến');
DECLARE @Brand TABLE (t int, i int, v nvarchar(50), PRIMARY KEY (t, i));
INSERT INTO @Brand VALUES (1,0,N'Honda Vision'),(1,1,N'Honda Air Blade'),(1,2,N'Yamaha Exciter'),(1,3,N'Honda Wave Alpha'),
    (1,4,N'Yamaha Sirius'),(1,5,N'Honda SH Mode'),(1,6,N'VinFast Feliz'),(1,7,N'Piaggio Vespa'),
    (2,0,N'Toyota Vios'),(2,1,N'Hyundai Accent'),(2,2,N'Kia Morning'),(2,3,N'Mazda CX-5'),(2,4,N'VinFast VF 5'),
    (2,5,N'Honda City'),(2,6,N'Ford Ranger'),(2,7,N'Toyota Fortuner'),
    (3,0,N'Giant'),(3,1,N'Asama'),(3,2,N'Yadea'),(3,3,N'Pega'),(3,4,N'Martin 107'),(3,5,N'Thống Nhất'),(3,6,N'VinFast Evo'),(3,7,N'Dibao');
DECLARE @Color TABLE (i int PRIMARY KEY, v nvarchar(20));
INSERT INTO @Color VALUES (0,N'Đen'),(1,N'Trắng'),(2,N'Đỏ'),(3,N'Xanh dương'),(4,N'Bạc'),(5,N'Xám'),(6,N'Vàng'),(7,N'Nâu');

-- Hàm sinh biển số (inline): kind 1 = xe máy, 2 = ô tô, 3 = xe đạp/điện; seq duy nhất
-- Xe máy: 59X1-123.45 ; Ô tô: 51F-123.45 ; Xe điện: 59E1-123.45
DECLARE @Prov TABLE (i int PRIMARY KEY, v nvarchar(3));
INSERT INTO @Prov VALUES (0,N'59'),(1,N'51'),(2,N'50'),(3,N'61'),(4,N'72'),(5,N'60'),(6,N'63'),(7,N'30'),(8,N'29');
DECLARE @SerMoto TABLE (i int PRIMARY KEY, v nvarchar(3));
INSERT INTO @SerMoto VALUES (0,N'X1'),(1,N'F1'),(2,N'G2'),(3,N'K1'),(4,N'N1'),(5,N'T1'),(6,N'V3'),(7,N'P2');
DECLARE @SerCar TABLE (i int PRIMARY KEY, v nvarchar(2));
INSERT INTO @SerCar VALUES (0,N'A'),(1,N'F'),(2,N'G'),(3,N'H'),(4,N'K');

-- ===================================================================================
-- 4. HỘ DÂN (60 hộ)
-- ===================================================================================
DECLARE @HH TABLE (h int PRIMARY KEY, HouseholdId int NULL, Members int, ExtraCar bit);

;WITH N AS (SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS h FROM sys.all_objects)
INSERT INTO Households (ApartmentCode, MaxVehicles, IsActive, Notes, CreatedAt)
SELECT x.Code,
       CASE WHEN N.h IN (5, 15, 25) THEN 3 WHEN N.h = 30 THEN 1 ELSE NULL END,
       CASE WHEN N.h IN (7, 23) THEN 0 ELSE 1 END,
       @Mark + CASE WHEN N.h IN (7, 23) THEN N' Hộ đã chuyển đi (ngưng hoạt động)'
                    WHEN N.h IN (5, 15, 25) THEN N' Hộ được phép 3 xe'
                    WHEN N.h = 30 THEN N' Hộ chỉ được 1 xe'
                    ELSE N'' END,
       DATEADD(DAY, -200 + N.h, @Now)
FROM N
CROSS APPLY (SELECT CHAR(65 + (N.h - 1) % 3) + N'-' + RIGHT(N'0' + CAST(2 + (N.h - 1) / 3 AS nvarchar(3)), 2)
                    + RIGHT(N'0' + CAST(1 + N.h % 6 AS nvarchar(3)), 2) AS Code) x
WHERE NOT EXISTS (SELECT 1 FROM Households e WHERE e.ApartmentCode = x.Code);

INSERT INTO @HH (h, HouseholdId, Members, ExtraCar)
SELECT ROW_NUMBER() OVER (ORDER BY HouseholdId), HouseholdId, 0, 0
FROM Households WHERE Notes LIKE N'[[]MẪU]%';
UPDATE @HH SET Members = CASE WHEN h % 5 = 0 THEN 3 WHEN h % 2 = 0 THEN 2 ELSE 1 END,
               ExtraCar = CASE WHEN h % 6 = 0 THEN 1 ELSE 0 END;

-- ===================================================================================
-- 5. KHÁCH HÀNG: cư dân (theo hộ) + khách vé tháng bên ngoài
-- ===================================================================================
DECLARE @Cust TABLE (k int PRIMARY KEY, CustomerId int NULL, HouseholdId int NULL, IsResident bit, MemberNo int, IsActive bit, h int NULL);

;WITH M AS (SELECT TOP (3) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS m FROM sys.all_objects)
INSERT INTO @Cust (k, HouseholdId, IsResident, MemberNo, IsActive, h)
SELECT ROW_NUMBER() OVER (ORDER BY hh.h, M.m), hh.HouseholdId, 1, M.m,
       CASE WHEN hh.h = 10 AND M.m = 1 THEN 0 ELSE 1 END, hh.h
FROM @HH hh JOIN M ON M.m <= hh.Members;

DECLARE @ResCount int = (SELECT COUNT(*) FROM @Cust);
;WITH N AS (SELECT TOP (45) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
INSERT INTO @Cust (k, HouseholdId, IsResident, MemberNo, IsActive, h)
SELECT @ResCount + n, NULL, 0, 1, CASE WHEN n = 3 THEN 0 ELSE 1 END, NULL FROM N;

DECLARE @CustMap TABLE (CustomerId int, k int);
MERGE INTO Customers AS t
USING (
    SELECT c.k, c.HouseholdId, c.IsResident, c.IsActive,
           ho.v + N' ' + de.v + N' ' + te.v AS FullName,
           N'09' + RIGHT(N'0000000' + CAST(30000000 + c.k * 7919 % 9000000 AS nvarchar(10)), 8) AS Phone,
           N'0' + RIGHT(N'00000000000' + CAST(79090000000 + c.k * 104729 AS nvarchar(20)), 11) AS Cccd
    FROM @Cust c
    JOIN @Ho ho ON ho.i = (c.k * 7) % 14
    JOIN @Dem de ON de.i = (c.k * 3) % 10
    JOIN @Ten te ON te.i = (c.k * 11) % 30
) AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (FullName, PhoneNumber, Email, IdentityCard, Type, HouseholdId, CreatedAt, IsActive, Notes)
    VALUES (s.FullName, s.Phone,
            CASE WHEN s.k % 3 = 0 THEN N'khach' + CAST(s.k AS nvarchar(10)) + N'@gmail.com' END,
            s.Cccd, CASE WHEN s.IsResident = 1 THEN 0 ELSE 1 END, s.HouseholdId,
            DATEADD(DAY, -180 + s.k % 60, @Now), s.IsActive,
            @Mark + CASE WHEN s.IsActive = 0 THEN N' Khách hàng đã bị khoá' ELSE N'' END)
OUTPUT inserted.CustomerId, s.k INTO @CustMap (CustomerId, k);

UPDATE c SET c.CustomerId = m.CustomerId FROM @Cust c JOIN @CustMap m ON m.k = c.k;

-- ===================================================================================
-- 6. PHƯƠNG TIỆN
-- ===================================================================================
DECLARE @Veh TABLE (v int IDENTITY(1,1) PRIMARY KEY, CustomerId int, k int, TypeId int, Plate nvarchar(20), VehicleId int NULL, HouseholdId int NULL, IsResident bit);

-- mỗi khách 1 xe chính
INSERT INTO @Veh (CustomerId, k, TypeId, HouseholdId, IsResident)
SELECT c.CustomerId, c.k,
       CASE WHEN c.IsResident = 1 THEN CASE WHEN c.k % 13 = 0 THEN @TBike WHEN c.k % 7 = 0 THEN @TCar ELSE @TMoto END
            ELSE CASE WHEN c.k % 20 = 0 THEN @TBike WHEN c.k % 2 = 0 THEN @TCar ELSE @TMoto END END,
       c.HouseholdId, c.IsResident
FROM @Cust c;

-- hộ có thêm 1 ô tô (thành viên đầu tiên)
INSERT INTO @Veh (CustomerId, k, TypeId, HouseholdId, IsResident)
SELECT c.CustomerId, c.k, @TCar, c.HouseholdId, 1
FROM @Cust c JOIN @HH hh ON hh.HouseholdId = c.HouseholdId
WHERE hh.ExtraCar = 1 AND c.MemberNo = 1;

UPDATE x SET x.Plate =
    CASE x.TypeId
        WHEN @TCar THEN p.v + sc.v + N'-' + LEFT(d.d5, 3) + N'.' + RIGHT(d.d5, 2)
        WHEN @TBike THEN p.v + N'E1-' + LEFT(d.d5, 3) + N'.' + RIGHT(d.d5, 2)
        ELSE p.v + sm.v + N'-' + LEFT(d.d5, 3) + N'.' + RIGHT(d.d5, 2)
    END
FROM @Veh x
CROSS APPLY (SELECT CAST(10000 + (x.v * 373) % 90000 AS nvarchar(5)) AS d5) d
JOIN @Prov p ON p.i = x.v % 9
JOIN @SerMoto sm ON sm.i = x.v % 8
JOIN @SerCar sc ON sc.i = x.v % 5;

-- bỏ những biển số đã tồn tại trong database
DELETE x FROM @Veh x
WHERE EXISTS (SELECT 1 FROM Vehicles e WHERE e.IsActive = 1 AND dbo.fn_NormalizePlate(e.LicensePlate) = dbo.fn_NormalizePlate(x.Plate));

DECLARE @VehMap TABLE (VehicleId int, v int);
MERGE INTO Vehicles AS t
USING (
    SELECT x.v, x.Plate, x.TypeId, x.CustomerId, b.v AS Brand, co.v AS Color
    FROM @Veh x
    JOIN @Brand b ON b.t = CASE x.TypeId WHEN @TCar THEN 2 WHEN @TBike THEN 3 ELSE 1 END AND b.i = x.v % 8
    JOIN @Color co ON co.i = (x.v * 5) % 8
) AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (LicensePlate, VehicleTypeId, Color, Brand, OwnerCustomerId, IsActive, CreatedAt)
    VALUES (s.Plate, s.TypeId, s.Color, s.Brand, s.CustomerId, 1, DATEADD(DAY, -170 + s.v % 50, @Now))
OUTPUT inserted.VehicleId, s.v INTO @VehMap (VehicleId, v);
UPDATE x SET x.VehicleId = m.VehicleId FROM @Veh x JOIN @VehMap m ON m.v = x.v;

-- Xe vượt định mức: trong hộ đang hoạt động, các xe xếp sau N xe đầu (theo VehicleId)
DECLARE @DefaultMax int = ISNULL((SELECT TOP 1 DefaultMaxVehiclesPerHousehold FROM ParkingSettings ORDER BY SettingsId), 2);
DECLARE @OverQuota TABLE (VehicleId int PRIMARY KEY);
INSERT INTO @OverQuota
SELECT r.VehicleId FROM (
    SELECT x.VehicleId, ROW_NUMBER() OVER (PARTITION BY x.HouseholdId ORDER BY x.VehicleId) AS rn
    FROM @Veh x JOIN Households hh ON hh.HouseholdId = x.HouseholdId
    WHERE x.IsResident = 1 AND hh.IsActive = 1
) r
JOIN @Veh x2 ON x2.VehicleId = r.VehicleId
WHERE r.rn > @DefaultMax;

-- ===================================================================================
-- 7. VÉ THÁNG (tất cả khách ngoài + 25 xe cư dân)
-- ===================================================================================
DECLARE @Tk TABLE (t int IDENTITY(1,1) PRIMARY KEY, CustomerId int, VehicleId int, Plate nvarchar(20), VehTypeId int,
                   TicketTypeId int, Dur int, StartDate datetime2, EndDate datetime2, Status int, Price decimal(18,2),
                   Code nvarchar(50), TicketId int NULL, Kind nvarchar(20));

INSERT INTO @Tk (CustomerId, VehicleId, Plate, VehTypeId)
SELECT x.CustomerId, x.VehicleId, x.Plate, x.TypeId FROM @Veh x
WHERE x.VehicleId IS NOT NULL AND x.TypeId <> @TBike
  AND (x.IsResident = 0 OR x.v % 5 = 1)
ORDER BY x.v;

UPDATE @Tk SET
    Dur = CASE t % 3 WHEN 0 THEN 1 WHEN 1 THEN 3 ELSE 6 END,
    Kind = CASE
        WHEN t % 20 = 0 THEN N'Cancelled'
        WHEN t % 20 = 1 THEN N'Suspended'
        WHEN t % 20 IN (2, 3) THEN N'ExpiredActive'   -- Status vẫn Active nhưng EndDate đã qua -> hiển thị Hết hạn
        WHEN t % 20 = 4 THEN N'Expired'
        WHEN t % 20 IN (5, 6) THEN N'Soon'            -- sắp hết hạn trong 15 ngày
        WHEN t = 9 THEN N'Mismatch'                   -- loại xe trên vé khác loại xe thật
        WHEN t = 12 THEN N'FutureStart'               -- vé bắt đầu hiệu lực từ tuần sau
        ELSE N'Active' END,
    Code = NULL;

UPDATE @Tk SET
    StartDate = CASE Kind WHEN N'ExpiredActive' THEN DATEADD(DAY, -150, @Now) WHEN N'Expired' THEN DATEADD(DAY, -200, @Now)
                          WHEN N'FutureStart' THEN DATEADD(DAY, 7, @Now) ELSE DATEADD(DAY, -100 - t % 20, @Now) END,
    EndDate = CASE Kind
        WHEN N'ExpiredActive' THEN DATEADD(DAY, -(t % 7) - 1, @Now)
        WHEN N'Expired' THEN DATEADD(DAY, -40, @Now)
        WHEN N'Soon' THEN DATEADD(DAY, 3 + t % 10, @Now)
        WHEN N'FutureStart' THEN DATEADD(MONTH, 1, DATEADD(DAY, 7, @Now))
        ELSE DATEADD(DAY, 20 + (t * 13) % 150, @Now) END,
    Status = CASE Kind WHEN N'Cancelled' THEN 3 WHEN N'Suspended' THEN 2 WHEN N'Expired' THEN 1 ELSE 0 END,
    TicketTypeId = CASE WHEN Kind = N'Mismatch' THEN CASE WHEN VehTypeId = @TCar THEN @TMoto ELSE @TCar END ELSE VehTypeId END;

-- Mã vé tháng VT-yyyy-0001 theo năm cấp (giờ VN), nối tiếp số lớn nhất đã có
;WITH b AS (SELECT SUBSTRING(TicketCode, 4, 4) AS Y, MAX(TRY_CAST(SUBSTRING(TicketCode, 9, 10) AS int)) AS M
            FROM MonthlyTickets WHERE TicketCode LIKE N'VT-[0-9][0-9][0-9][0-9]-%' GROUP BY SUBSTRING(TicketCode, 4, 4)),
      n AS (SELECT t.t, CAST(YEAR(DATEADD(HOUR, 7, t.StartDate)) AS nvarchar(4)) AS Y,
                   ROW_NUMBER() OVER (PARTITION BY YEAR(DATEADD(HOUR, 7, t.StartDate)) ORDER BY t.StartDate, t.t) AS rn
            FROM @Tk t)
UPDATE t SET t.Code = N'VT-' + n.Y + N'-' + RIGHT(N'0000' + CAST(ISNULL(b.M, 0) + n.rn AS nvarchar(10)), 4)
FROM @Tk t JOIN n ON n.t = t.t LEFT JOIN b ON b.Y = n.Y COLLATE DATABASE_DEFAULT;

UPDATE t SET t.Price = CASE t.Dur WHEN 1 THEN pr.Monthly1Price WHEN 3 THEN pr.Monthly3Price ELSE pr.Monthly6Price END
FROM @Tk t JOIN PricingRules pr ON pr.VehicleTypeId = t.TicketTypeId;

DECLARE @TkMap TABLE (TicketId int, t int);
MERGE INTO MonthlyTickets AS d
USING @Tk AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (TicketCode, CustomerId, RegisteredLicensePlate, VehicleId, DurationMonths, VehicleTypeId, StartDate, EndDate, MonthlyPrice, Status, Notes, CreatedAt)
    VALUES (s.Code, s.CustomerId, s.Plate, s.VehicleId, s.Dur, s.TicketTypeId, s.StartDate, s.EndDate, ISNULL(s.Price, 0), s.Status,
            CASE s.Kind WHEN N'Mismatch' THEN N'Vé đăng ký sai loại xe (để test cảnh báo)'
                        WHEN N'FutureStart' THEN N'Vé bắt đầu hiệu lực từ tuần sau' END,
            s.StartDate)
OUTPUT inserted.TicketId, s.t INTO @TkMap (TicketId, t);
UPDATE t SET t.TicketId = m.TicketId FROM @Tk t JOIN @TkMap m ON m.t = t.t;

-- Lịch sử vé: cấp mới
INSERT INTO MonthlyTicketHistories (TicketId, Action, NewVehicleId, NewLicensePlate, NewEndDate, AddedMonths, Amount, PaymentMethod, Reason, PerformedByUserId, CreatedAt)
SELECT TicketId, 1, VehicleId, Plate, DATEADD(MONTH, Dur, StartDate), Dur, ISNULL(Price, 0), CASE WHEN t % 3 = 0 THEN 1 ELSE 0 END,
       NULL, CASE WHEN t % 2 = 0 THEN @MgrHoa ELSE @MgrKhanh END, StartDate
FROM @Tk;
-- Gia hạn (vé còn hạn có EndDate xa)
INSERT INTO MonthlyTicketHistories (TicketId, Action, OldEndDate, NewEndDate, AddedMonths, Amount, PaymentMethod, PerformedByUserId, CreatedAt)
SELECT TicketId, 2, DATEADD(MONTH, Dur, StartDate), EndDate, Dur, ISNULL(Price, 0), 0, @MgrHoa, DATEADD(DAY, -15 - t % 10, @Now)
FROM @Tk WHERE Kind IN (N'Active', N'Soon') AND t % 2 = 1;
-- Đổi biển số
INSERT INTO MonthlyTicketHistories (TicketId, Action, OldLicensePlate, NewLicensePlate, NewVehicleId, Amount, Reason, PerformedByUserId, CreatedAt)
SELECT TicketId, 3, N'51K-999.' + RIGHT(N'0' + CAST(t AS nvarchar(3)), 2), Plate, VehicleId, 0, N'Khách đổi xe mới', @MgrKhanh, DATEADD(DAY, -30, @Now)
FROM @Tk WHERE t IN (11, 17);
-- Khoá / mở khoá / huỷ
INSERT INTO MonthlyTicketHistories (TicketId, Action, Amount, Reason, PerformedByUserId, CreatedAt)
SELECT TicketId, 4, 0, N'Khách báo mất thẻ', @MgrHoa, DATEADD(DAY, -5, @Now) FROM @Tk WHERE Kind = N'Suspended';
INSERT INTO MonthlyTicketHistories (TicketId, Action, Amount, Reason, PerformedByUserId, CreatedAt)
SELECT TicketId, 4, 0, N'Tạm khoá do chưa đóng phí', @MgrKhanh, DATEADD(DAY, -50, @Now) FROM @Tk WHERE Kind = N'Active' AND t % 7 = 0;
INSERT INTO MonthlyTicketHistories (TicketId, Action, Amount, Reason, PerformedByUserId, CreatedAt)
SELECT TicketId, 5, 0, N'Đã đóng phí, mở khoá lại', @MgrKhanh, DATEADD(DAY, -45, @Now) FROM @Tk WHERE Kind = N'Active' AND t % 7 = 0;
INSERT INTO MonthlyTicketHistories (TicketId, Action, Amount, Reason, PerformedByUserId, CreatedAt)
SELECT TicketId, 6, 0, N'Khách chuyển đi, huỷ vé', @MgrHoa, DATEADD(DAY, -10, @Now) FROM @Tk WHERE Kind = N'Cancelled';

-- ===================================================================================
-- 8. DANH SÁCH CẤM
-- ===================================================================================
INSERT INTO BlacklistedVehicles (LicensePlate, Reason, Notes, CustomerId, CreatedAt, IsActive)
SELECT v.Plate, v.Reason, @Mark + ISNULL(N' ' + v.Note, N''), NULL, DATEADD(DAY, -v.Days, @Now), v.Active
FROM (VALUES
    (N'51G-666.88', N'Gây rối, đánh nhau trong bãi', NULL, 40, 1),
    (N'59X2-444.44', N'Trộm cắp phụ tùng xe khác', N'Đã báo công an phường', 25, 1),
    (N'30A12345', N'Nợ phí gửi xe nhiều lần', N'Biển số nhập không dấu để test chuẩn hoá (khớp 30A-123.45)', 12, 1),
    (N'61F1-777.77', N'Sử dụng vé tháng giả', NULL, 8, 1),
    (N'72A-321.00', N'Đỗ xe chắn lối thoát hiểm nhiều lần', N'Đã gỡ cấm sau khi cam kết', 60, 0),
    (N'50K1-123.98', N'Nghi vấn xe không chính chủ', N'Đã xác minh, gỡ cấm', 90, 0)
) AS v(Plate, Reason, Note, Days, Active);

-- Một xe của khách hàng (có vé tháng) bị cấm -> test chặn xe dù có vé
INSERT INTO BlacklistedVehicles (LicensePlate, Reason, Notes, CustomerId, CreatedAt, IsActive)
SELECT TOP (1) t.Plate, N'Vi phạm nội quy, tạm cấm vào bãi', @Mark + N' Xe có vé tháng còn hạn', t.CustomerId, DATEADD(DAY, -3, @Now), 1
FROM @Tk t WHERE t.Kind = N'Active' AND t.t > 20 ORDER BY t.t DESC;

DECLARE @BlockedPlates TABLE (Norm nvarchar(30) PRIMARY KEY);
INSERT INTO @BlockedPlates SELECT DISTINCT dbo.fn_NormalizePlate(LicensePlate) FROM BlacklistedVehicles WHERE IsActive = 1;

-- ===================================================================================
-- 9. CA TRỰC 60 NGÀY QUA (3 ca/ngày) + 1 ca đang mở
-- ===================================================================================
DECLARE @Sh TABLE (d int, s int, ShiftId int NULL, OpUser int, OpenUtc datetime2, CloseUtc datetime2, IsOpen bit, PRIMARY KEY (d, s));

;WITH D AS (SELECT TOP (61) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS d FROM sys.all_objects),
      S AS (SELECT 0 AS s UNION ALL SELECT 1 UNION ALL SELECT 2)
INSERT INTO @Sh (d, s, OpUser, OpenUtc, CloseUtc, IsOpen)
SELECT D.d, S.s, o.UserId,
       DATEADD(HOUR, 6 + 8 * S.s - 7, CAST(DATEADD(DAY, -D.d, @VnToday) AS datetime2)),
       DATEADD(HOUR, 14 + 8 * S.s - 7, CAST(DATEADD(DAY, -D.d, @VnToday) AS datetime2)),
       0
FROM D CROSS JOIN S
JOIN @Ops o ON o.Idx = (D.d * 3 + S.s) % 4;

-- bỏ ca chưa bắt đầu; ca chứa thời điểm hiện tại là ca đang mở (giao cho nv.an)
DELETE FROM @Sh WHERE OpenUtc > @Now;
UPDATE @Sh SET IsOpen = 1, OpUser = @OpAn WHERE OpenUtc <= @Now AND CloseUtc > @Now;
-- ca đêm trước 06:00 VN: ca bắt đầu từ hôm trước (d = -1 không có) -> đã được tạo ở d+1 với s = 2

IF EXISTS (SELECT 1 FROM Shifts WHERE OpenedByUserId = @OpAn AND Status = 0)
    UPDATE @Sh SET IsOpen = 0, CloseUtc = @Now WHERE IsOpen = 1; -- nv.an đang có ca thật: không tạo thêm ca mở

-- tránh 2 ca cùng mở của một người: các ca trước của nv.an đều đã đóng
DECLARE @ShMap TABLE (ShiftId int, d int, s int);
MERGE INTO Shifts AS t
USING @Sh AS x ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (OpenedByUserId, OpenedAt, BeginningCash, ClosedAt, Status)
    VALUES (x.OpUser, x.OpenUtc, CASE WHEN x.s = 2 THEN 300000 ELSE 500000 END,
            CASE WHEN x.IsOpen = 1 THEN NULL ELSE x.CloseUtc END,
            CASE WHEN x.IsOpen = 1 THEN 0 ELSE 1 END)
OUTPUT inserted.ShiftId, x.d, x.s INTO @ShMap (ShiftId, d, s);
UPDATE x SET x.ShiftId = m.ShiftId FROM @Sh x JOIN @ShMap m ON m.d = x.d AND m.s = x.s;

-- Mã ca CA-yyMMdd-01 theo thứ tự giờ mở ca trong ngày (giờ VN), nối tiếp các ca đã có trong ngày
;WITH b AS (SELECT SUBSTRING(ShiftCode, 4, 6) AS D, MAX(TRY_CAST(SUBSTRING(ShiftCode, 11, 10) AS int)) AS M
            FROM Shifts WHERE ShiftCode LIKE N'CA-[0-9][0-9][0-9][0-9][0-9][0-9]-%' GROUP BY SUBSTRING(ShiftCode, 4, 6)),
      n AS (SELECT sh.ShiftId, CONVERT(nvarchar(6), DATEADD(HOUR, 7, sh.OpenedAt), 12) AS D,
                   ROW_NUMBER() OVER (PARTITION BY CAST(DATEADD(HOUR, 7, sh.OpenedAt) AS date) ORDER BY sh.OpenedAt, sh.ShiftId) AS rn
            FROM Shifts sh JOIN @ShMap m ON m.ShiftId = sh.ShiftId)
UPDATE sh SET sh.ShiftCode = N'CA-' + n.D + N'-' + RIGHT(N'00' + CAST(ISNULL(b.M, 0) + n.rn AS nvarchar(10)), 2)
FROM Shifts sh JOIN n ON n.ShiftId = sh.ShiftId LEFT JOIN b ON b.D = n.D;

DECLARE @OpenShiftId int = (SELECT ShiftId FROM @Sh WHERE IsOpen = 1);

-- ===================================================================================
-- 10. LƯỢT GỬI ĐÃ HOÀN THÀNH (60 ngày)
-- ===================================================================================
IF OBJECT_ID('tempdb..#S') IS NOT NULL DROP TABLE #S;
CREATE TABLE #S (
    id int IDENTITY(1,1) PRIMARY KEY,
    d int, r1 int, r2 int, r3 int, r4 int, r5 int, r6 int,
    Category int,            -- 0 vãng lai, 1 cư dân, 2 vé tháng
    TypeId int, Plate nvarchar(20) COLLATE DATABASE_DEFAULT, CustomerId int NULL, TicketCode nvarchar(50) COLLATE DATABASE_DEFAULT, IsOverQuota bit DEFAULT 0,
    InUtc datetime2, OutUtc datetime2, Minutes int, Fee decimal(18,2), Method int,
    SlotId int NULL, InShift int NULL, OutShift int NULL, InUser int NULL, OutUser int NULL, SessionId int NULL);

;WITH D AS (SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS d FROM sys.all_objects),
      N AS (SELECT TOP (160) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
INSERT INTO #S (d, r1, r2, r3, r4, r5, r6)
SELECT D.d, ABS(CHECKSUM(NEWID())) % 100, ABS(CHECKSUM(NEWID())) % 100000, ABS(CHECKSUM(NEWID())) % 100000,
       ABS(CHECKSUM(NEWID())) % 100, ABS(CHECKSUM(NEWID())) % 100000, ABS(CHECKSUM(NEWID())) % 100
FROM D JOIN N ON N.n <= 90 + (D.d * 37) % 50
           + CASE WHEN DATEPART(WEEKDAY, DATEADD(DAY, -D.d, @VnToday)) IN (1, 7) THEN 30 ELSE 0 END;

UPDATE #S SET Category = CASE WHEN r1 < 58 THEN 0 WHEN r1 < 85 THEN 1 ELSE 2 END;

-- Giờ vào: phân bố theo giờ cao điểm (7-9h, 11-13h, 17-19h giờ VN)
UPDATE #S SET InUtc = DATEADD(MINUTE,
        (CASE WHEN r4 < 25 THEN 7 * 60 + r2 % 120 WHEN r4 < 40 THEN 11 * 60 + r2 % 120
              WHEN r4 < 65 THEN 17 * 60 + r2 % 150 ELSE 5 * 60 + r2 % (18 * 60) END) - 7 * 60,
        CAST(DATEADD(DAY, -d, @VnToday) AS datetime2));

-- Thời lượng gửi
UPDATE #S SET Minutes = CASE
        WHEN r6 < 5  THEN 3 + r5 % 12                 -- trong 15 phút ân hạn
        WHEN r6 < 45 THEN 30 + r5 % 210               -- dưới 4 giờ
        WHEN r6 < 75 THEN 240 + r5 % 360              -- 4 - 10 giờ
        WHEN r6 < 90 THEN 600 + r5 % 840              -- 10 - 24 giờ
        WHEN r6 < 97 THEN 1440 + r5 % 1440            -- 1 - 2 ngày
        ELSE 2880 + r5 % 2880 END;                    -- 2 - 4 ngày
UPDATE #S SET OutUtc = DATEADD(MINUTE, Minutes, InUtc);
DELETE FROM #S WHERE OutUtc > DATEADD(MINUTE, -10, @Now);

-- Gán xe: cư dân
;WITH R AS (SELECT x.*, ROW_NUMBER() OVER (ORDER BY x.v) - 1 AS rn, COUNT(*) OVER () AS cnt
            FROM @Veh x JOIN Households hh ON hh.HouseholdId = x.HouseholdId
            WHERE x.IsResident = 1 AND x.VehicleId IS NOT NULL AND hh.IsActive = 1)
UPDATE s SET s.TypeId = R.TypeId, s.Plate = R.Plate, s.CustomerId = R.CustomerId,
             s.IsOverQuota = CASE WHEN EXISTS (SELECT 1 FROM @OverQuota q WHERE q.VehicleId = R.VehicleId) THEN 1 ELSE 0 END
FROM #S s JOIN R ON R.rn = s.r3 % R.cnt
WHERE s.Category = 1;

-- Vé tháng còn hạn trong suốt 60 ngày qua (vé ngoài, đúng loại xe)
;WITH R AS (SELECT t.*, ROW_NUMBER() OVER (ORDER BY t.t) - 1 AS rn, COUNT(*) OVER () AS cnt
            FROM @Tk t JOIN @Veh x ON x.VehicleId = t.VehicleId
            WHERE t.Kind IN (N'Active', N'Soon') AND x.IsResident = 0 AND t.StartDate <= DATEADD(DAY, -61, @Now))
UPDATE s SET s.TypeId = R.VehTypeId, s.Plate = R.Plate, s.CustomerId = R.CustomerId, s.TicketCode = R.Code
FROM #S s JOIN R ON R.rn = s.r3 % R.cnt
WHERE s.Category = 2;

-- Vãng lai: 1.500 biển số ngẫu nhiên lặp lại
UPDATE #S SET Category = 0 WHERE Category IN (1, 2) AND Plate IS NULL;
UPDATE s SET
    s.TypeId = CASE WHEN s.r4 < 75 THEN @TMoto WHEN s.r4 < 90 THEN @TCar ELSE @TBike END
FROM #S s WHERE s.Category = 0;
UPDATE s SET s.Plate =
    CASE s.TypeId
        WHEN @TCar THEN p.v + sc.v + N'-' + LEFT(dd.d5, 3) + N'.' + RIGHT(dd.d5, 2)
        WHEN @TBike THEN p.v + N'E2-' + LEFT(dd.d5, 3) + N'.' + RIGHT(dd.d5, 2)
        ELSE p.v + sm.v + N'-' + LEFT(dd.d5, 3) + N'.' + RIGHT(dd.d5, 2) END
FROM #S s
CROSS APPLY (SELECT 1000 + s.r3 % 1500 AS q) qq
CROSS APPLY (SELECT CAST(10000 + (qq.q * 373) % 90000 AS nvarchar(5)) AS d5) dd
JOIN @Prov p ON p.i = (qq.q / 3) % 9
JOIN @SerMoto sm ON sm.i = (qq.q / 2) % 8
JOIN @SerCar sc ON sc.i = qq.q % 5
WHERE s.Category = 0;
-- không tạo lượt gửi cho biển số đang bị cấm
DELETE s FROM #S s WHERE EXISTS (SELECT 1 FROM @BlockedPlates b WHERE b.Norm = dbo.fn_NormalizePlate(s.Plate));

-- Tính phí theo công thức của PricingEngine (chu kỳ 24h, khung giờ, trần ngày, ân hạn, qua đêm, vượt định mức)
UPDATE s SET s.Fee =
    CASE
        WHEN s.Category = 2 THEN 0
        WHEN s.IsOverQuota = 1 THEN CEILING(s.Minutes / 1440.0) * pr.OverQuotaDailyPrice
                                    + DATEDIFF(DAY, DATEADD(HOUR, 7, s.InUtc), DATEADD(HOUR, 7, s.OutUtc)) * pr.OvernightSurcharge
        WHEN s.Minutes <= pr.GracePeriodMinutes THEN 0
        ELSE (s.Minutes / 1440) * pr.DailyPrice
             + CASE WHEN s.Minutes % 1440 = 0 THEN 0
                    ELSE CASE WHEN CEILING((s.Minutes % 1440) / 240.0) * pr.Block4hPrice > pr.DailyPrice THEN pr.DailyPrice
                              ELSE CEILING((s.Minutes % 1440) / 240.0) * pr.Block4hPrice END END
             + DATEDIFF(DAY, DATEADD(HOUR, 7, s.InUtc), DATEADD(HOUR, 7, s.OutUtc)) * pr.OvernightSurcharge
    END
FROM #S s JOIN PricingRules pr ON pr.VehicleTypeId = s.TypeId;

UPDATE #S SET Method = CASE WHEN Fee <= 0 THEN 3 WHEN r5 % 100 < 62 THEN 0 ELSE 1 END;

-- Ca vào / ca ra và nhân viên
UPDATE s SET s.InShift = x.ShiftId, s.InUser = x.OpUser
FROM #S s JOIN @Sh x ON s.InUtc >= x.OpenUtc AND s.InUtc < x.CloseUtc;
UPDATE s SET s.OutShift = x.ShiftId, s.OutUser = x.OpUser
FROM #S s JOIN @Sh x ON s.OutUtc >= x.OpenUtc AND s.OutUtc < x.CloseUtc;
DELETE FROM #S WHERE OutShift IS NULL OR TypeId IS NULL OR Plate IS NULL;

-- Ô đỗ (đúng loại xe và đối tượng khu)
;WITH Sl AS (
    SELECT sl.SlotId, sl.VehicleTypeId, z.Audience,
           ROW_NUMBER() OVER (PARTITION BY sl.VehicleTypeId, CASE WHEN z.Audience = 1 THEN 1 ELSE 0 END ORDER BY sl.SlotId) - 1 AS rn,
           COUNT(*) OVER (PARTITION BY sl.VehicleTypeId, CASE WHEN z.Audience = 1 THEN 1 ELSE 0 END) AS cnt
    FROM ParkingSlots sl JOIN ParkingZones z ON z.ZoneId = sl.ZoneId)
UPDATE s SET s.SlotId = Sl.SlotId
FROM #S s JOIN Sl ON Sl.VehicleTypeId = s.TypeId
                 AND (CASE WHEN Sl.Audience = 1 THEN 1 ELSE 0 END) = CASE WHEN s.Category = 1 AND s.r6 % 2 = 0 THEN 1 ELSE 0 END
                 AND Sl.rn = s.r2 % Sl.cnt;
UPDATE s SET s.SlotId = x.SlotId
FROM #S s CROSS APPLY (SELECT TOP 1 sl.SlotId FROM ParkingSlots sl JOIN ParkingZones z ON z.ZoneId = sl.ZoneId
                       WHERE sl.VehicleTypeId = s.TypeId AND z.Audience <> 1 ORDER BY sl.SlotId) x
WHERE s.SlotId IS NULL;

UPDATE #S SET TicketCode = N'TMP-S' + CAST(id AS nvarchar(10))   -- đánh số VL-yyMMdd-0001 ở cuối script
WHERE TicketCode IS NULL;

-- Ghi lượt gửi (TicketCode của vé tháng không duy nhất nên ánh xạ bằng MERGE ... OUTPUT)
DECLARE @SesMap TABLE (SessionId int, id int PRIMARY KEY);
MERGE INTO ParkingSessions AS t
USING (
    SELECT s.*, (SELECT TOP 1 v.VersionId FROM PricingPolicyVersions v WHERE v.VehicleTypeId = s.TypeId ORDER BY v.VersionNumber DESC) AS VersionId
    FROM #S s
) AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (TicketCode, LicensePlate, VehicleTypeId, SlotId, CheckInTime, CheckOutTime, TotalFee, PricingPolicyVersionId,
            Status, PaymentMethod, CustomerId, IsMonthlyPass, CustomerType, CreatedByUserId)
    VALUES (s.TicketCode, s.Plate, s.TypeId, s.SlotId, s.InUtc, s.OutUtc, s.Fee, s.VersionId,
            1, s.Method, s.CustomerId, CASE WHEN s.Category = 2 THEN 1 ELSE 0 END,
            CASE WHEN s.Category = 1 THEN 0 ELSE 1 END, ISNULL(s.InUser, s.OutUser))
OUTPUT inserted.SessionId, s.id INTO @SesMap (SessionId, id);
UPDATE s SET s.SessionId = m.SessionId FROM #S s JOIN @SesMap m ON m.id = s.id;

-- Một số lượt bị huỷ (xe vào nhầm, quay đầu)
UPDATE ps SET ps.Status = 2, ps.TotalFee = 0, ps.PaymentMethod = 3
FROM ParkingSessions ps JOIN #S s ON s.SessionId = ps.SessionId
WHERE s.id % 211 = 0;

-- Giao dịch thu phí trong ca (mỗi lượt hoàn thành một giao dịch, kể cả miễn phí = 0đ)
INSERT INTO FinancialTransactions (TransactionCode, ShiftId, ParkingSessionId, CreatedByUserId, Type, PaymentMethod, Amount, CreatedAt, ReferenceCode)
SELECT N'TMP-F' + CAST(s.SessionId AS nvarchar(10)), s.OutShift, s.SessionId, s.OutUser, 0, s.Method,
       CASE WHEN s.Method = 3 THEN 0 ELSE s.Fee END, s.OutUtc,
       CASE WHEN s.Method = 1 THEN N'SPTX-' + CAST(s.SessionId AS nvarchar(10)) END
FROM #S s WHERE s.id % 211 <> 0;

-- Thanh toán VietQR: Payment + PaymentTransaction đã thanh toán
DECLARE @PayMap TABLE (PaymentId int, SessionId int, Status int);
MERGE INTO Payments AS t
USING (
    SELECT s.SessionId, s.Fee, s.OutShift, s.OutUser, s.OutUtc, s.Plate, 2 AS Status, 0 AS Early FROM #S s WHERE s.Method = 1 AND s.id % 211 <> 0
    UNION ALL  -- lần thanh toán trước đó bị khách huỷ / hết hạn (6% lượt QR)
    SELECT s.SessionId, s.Fee, s.OutShift, s.OutUser, s.OutUtc, s.Plate, CASE WHEN s.r2 % 2 = 0 THEN 4 ELSE 5 END, 1
    FROM #S s WHERE s.Method = 1 AND s.id % 211 <> 0 AND s.r5 % 100 >= 94
) AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (SessionId, ShiftId, CheckoutUserId, Amount, Currency, Status, PaymentMethod, Description, CreatedAt, UpdatedAt, PaidAt, ExpiredAt)
    VALUES (s.SessionId, s.OutShift, s.OutUser, s.Fee, N'VND', s.Status, 1,
            @Mark + N' Thu phí gửi xe ' + s.Plate,
            DATEADD(MINUTE, CASE WHEN s.Early = 1 THEN -8 ELSE -2 END, s.OutUtc),
            DATEADD(MINUTE, CASE WHEN s.Early = 1 THEN -3 ELSE 0 END, s.OutUtc),
            CASE WHEN s.Status = 2 THEN s.OutUtc END,
            CASE WHEN s.Status = 5 THEN DATEADD(MINUTE, -3, s.OutUtc) END)
OUTPUT inserted.PaymentId, s.SessionId, s.Status INTO @PayMap (PaymentId, SessionId, Status);

INSERT INTO PaymentTransactions (PaymentId, TransactionReference, GatewayTransactionId, GatewayOrderCode, Amount, Currency, Status,
                                 AccountNumber, AccountName, BankCode, CreatedAt, UpdatedAt, CompletedAt)
SELECT m.PaymentId,
       CASE WHEN m.Status = 2 THEN N'SPTX-' + CAST(m.SessionId AS nvarchar(10)) ELSE N'SPTX-' + CAST(m.SessionId AS nvarchar(10)) + N'-X' END,
       CASE WHEN m.Status = 2 THEN N'FT' + CAST(900000000 + m.PaymentId AS nvarchar(20)) END,
       CAST(70000000 + m.PaymentId AS nvarchar(20)), p.Amount, N'VND',
       CASE m.Status WHEN 2 THEN 2 ELSE 4 END,
       N'0123456789', N'BAN QUAN LY BAI XE SMARTPS', N'MBBank', p.CreatedAt, p.UpdatedAt, p.PaidAt
FROM @PayMap m JOIN Payments p ON p.PaymentId = m.PaymentId;

-- Hoàn tiền (khoảng 1% lượt có phí): tiền mặt ghi giao dịch âm; QR chuyển Payment sang Refunded
INSERT INTO FinancialTransactions (TransactionCode, ShiftId, ParkingSessionId, CreatedByUserId, Type, PaymentMethod, Amount, CreatedAt, Note)
SELECT N'TMP-R' + CAST(s.SessionId AS nvarchar(10)), s.OutShift, s.SessionId, s.OutUser, 1, s.Method,
       -CASE WHEN s.r6 % 2 = 0 THEN s.Fee ELSE ROUND(s.Fee / 2, -3) END, DATEADD(MINUTE, 20, s.OutUtc),
       CASE WHEN s.r6 % 2 = 0 THEN N'Hoàn toàn bộ: thu nhầm xe vé tháng' ELSE N'Hoàn một phần: tính sai giờ vào' END
FROM #S s WHERE s.Fee > 0 AND s.id % 97 = 5 AND s.id % 211 <> 0 AND DATEADD(MINUTE, 20, s.OutUtc) < @Now;
UPDATE p SET p.Status = 6, p.UpdatedAt = DATEADD(MINUTE, 20, p.PaidAt)
FROM Payments p JOIN #S s ON s.SessionId = p.SessionId
WHERE p.Status = 2 AND s.Method = 1 AND s.Fee > 0 AND s.id % 97 = 5 AND DATEADD(MINUTE, 20, s.OutUtc) < @Now;

-- Phí sự cố (mất vé, đền bù) và điều chỉnh tiền ca
INSERT INTO FinancialTransactions (TransactionCode, ShiftId, CreatedByUserId, Type, PaymentMethod, Amount, CreatedAt, Note)
SELECT N'TMP-I' + CAST(x.ShiftId AS nvarchar(10)), x.ShiftId, x.OpUser, 3, 0, 50000, DATEADD(HOUR, 3, x.OpenUtc), N'Phạt mất vé xe theo nội quy'
FROM @Sh x WHERE x.d % 9 = 4 AND x.s = 1 AND DATEADD(HOUR, 3, x.OpenUtc) < @Now;
INSERT INTO FinancialTransactions (TransactionCode, ShiftId, CreatedByUserId, Type, PaymentMethod, Amount, CreatedAt, Note)
SELECT N'TMP-J' + CAST(x.ShiftId AS nvarchar(10)), x.ShiftId, CASE WHEN x.d % 2 = 0 THEN @MgrHoa ELSE @MgrKhanh END, 2, 0,
       CASE WHEN x.d % 4 = 0 THEN 20000 ELSE -10000 END, DATEADD(MINUTE, 30, x.CloseUtc),
       CASE WHEN x.d % 4 = 0 THEN N'Điều chỉnh tăng: thu bổ sung lượt chưa ghi nhận' ELSE N'Điều chỉnh giảm: trả lại tiền thừa cho khách' END
FROM @Sh x WHERE x.d % 7 = 3 AND x.s = 0 AND x.IsOpen = 0 AND x.d >= 4;

-- Mã giao dịch GD-yyMMdd-0001 theo thứ tự thời gian trong ngày (giờ VN), nối tiếp các mã đã có
;WITH b AS (SELECT SUBSTRING(TransactionCode, 4, 6) AS D, MAX(TRY_CAST(SUBSTRING(TransactionCode, 11, 10) AS int)) AS M
            FROM FinancialTransactions WHERE TransactionCode LIKE N'GD-[0-9][0-9][0-9][0-9][0-9][0-9]-%' GROUP BY SUBSTRING(TransactionCode, 4, 6)),
      n AS (SELECT TransactionId, CONVERT(nvarchar(6), DATEADD(HOUR, 7, CreatedAt), 12) AS D,
                   ROW_NUMBER() OVER (PARTITION BY CAST(DATEADD(HOUR, 7, CreatedAt) AS date) ORDER BY CreatedAt, TransactionId) AS rn
            FROM FinancialTransactions WHERE TransactionCode LIKE N'TMP-%')
UPDATE ft SET ft.TransactionCode = N'GD-' + n.D + N'-' + RIGHT(N'0000' + CAST(ISNULL(b.M, 0) + n.rn AS nvarchar(10)), 4)
FROM FinancialTransactions ft JOIN n ON n.TransactionId = ft.TransactionId LEFT JOIN b ON b.D = n.D;

-- ===================================================================================
-- 11. CHỐT CA: tiền dự kiến, tiền thực tế, chênh lệch, duyệt ca
-- ===================================================================================
UPDATE sh SET
    sh.ExpectedCash = sh.BeginningCash + ISNULL(c.CashSum, 0),
    sh.ActualCash = sh.BeginningCash + ISNULL(c.CashSum, 0)
                    + CASE WHEN x.d % 11 = 2 THEN -50000 WHEN x.d % 13 = 5 THEN 20000 WHEN x.d % 5 = 1 AND x.s = 2 THEN -5000 ELSE 0 END,
    sh.Difference = CASE WHEN x.d % 11 = 2 THEN -50000 WHEN x.d % 13 = 5 THEN 20000 WHEN x.d % 5 = 1 AND x.s = 2 THEN -5000 ELSE 0 END,
    sh.Status = CASE WHEN x.d >= 3 THEN 2 ELSE 1 END,
    sh.ReviewedByUserId = CASE WHEN x.d >= 3 THEN CASE WHEN x.d % 2 = 0 THEN @MgrHoa ELSE @MgrKhanh END END,
    sh.ReviewedAt = CASE WHEN x.d >= 3 THEN DATEADD(HOUR, 10, x.CloseUtc) END,
    sh.ManagerNote = CASE WHEN x.d < 3 THEN NULL
                          WHEN x.d % 11 = 2 THEN N'Thiếu 50.000đ, nhân viên đã giải trình và nộp bù'
                          WHEN x.d % 13 = 5 THEN N'Thừa 20.000đ, chưa xác định nguồn, đã ghi nhận'
                          WHEN x.d % 5 = 1 AND x.s = 2 THEN N'Lệch tiền lẻ 5.000đ'
                          ELSE N'Đã kiểm tra, khớp tiền' END
FROM Shifts sh
JOIN @Sh x ON x.ShiftId = sh.ShiftId
OUTER APPLY (SELECT SUM(ft.Amount) AS CashSum FROM FinancialTransactions ft WHERE ft.ShiftId = sh.ShiftId AND ft.PaymentMethod = 0) c
WHERE x.IsOpen = 0;

-- ===================================================================================
-- 12. XE ĐANG TRONG BÃI
-- ===================================================================================
IF OBJECT_ID('tempdb..#A') IS NOT NULL DROP TABLE #A;
CREATE TABLE #A (id int IDENTITY(1,1) PRIMARY KEY, Category int, TypeId int, Plate nvarchar(20) COLLATE DATABASE_DEFAULT, CustomerId int NULL,
                 TicketCode nvarchar(50) COLLATE DATABASE_DEFAULT NULL, InUtc datetime2, Note nvarchar(100) COLLATE DATABASE_DEFAULT, SlotId int NULL, SessionId int NULL);

-- Cư dân (10 xe máy, 2 ô tô) - không trùng xe vượt định mức, không có vé tháng
INSERT INTO #A (Category, TypeId, Plate, CustomerId, InUtc, Note)
SELECT TOP (2) 1, x.TypeId, x.Plate, x.CustomerId, DATEADD(MINUTE, -(30 + x.v * 37 % 600), @Now), N'Cư dân'
FROM @Veh x JOIN Households hh ON hh.HouseholdId = x.HouseholdId
WHERE x.IsResident = 1 AND hh.IsActive = 1 AND x.TypeId = @TCar AND x.VehicleId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM @OverQuota q WHERE q.VehicleId = x.VehicleId)
  AND NOT EXISTS (SELECT 1 FROM @Tk t WHERE t.VehicleId = x.VehicleId)
ORDER BY x.v DESC;
INSERT INTO #A (Category, TypeId, Plate, CustomerId, InUtc, Note)
SELECT TOP (10) 1, x.TypeId, x.Plate, x.CustomerId, DATEADD(MINUTE, -(30 + x.v * 37 % 600), @Now), N'Cư dân'
FROM @Veh x JOIN Households hh ON hh.HouseholdId = x.HouseholdId
WHERE x.IsResident = 1 AND hh.IsActive = 1 AND x.TypeId = @TMoto AND x.VehicleId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM @OverQuota q WHERE q.VehicleId = x.VehicleId)
  AND NOT EXISTS (SELECT 1 FROM @Tk t WHERE t.VehicleId = x.VehicleId)
ORDER BY x.v DESC;
-- Cư dân gửi quá hạn 3 ngày
INSERT INTO #A (Category, TypeId, Plate, CustomerId, InUtc, Note)
SELECT TOP (1) 1, x.TypeId, x.Plate, x.CustomerId, DATEADD(HOUR, -75, @Now), N'Cư dân gửi quá hạn'
FROM @Veh x JOIN Households hh ON hh.HouseholdId = x.HouseholdId
WHERE x.IsResident = 1 AND hh.IsActive = 1 AND x.TypeId = @TMoto AND x.VehicleId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM #A a WHERE a.Plate = x.Plate)
  AND NOT EXISTS (SELECT 1 FROM @OverQuota q WHERE q.VehicleId = x.VehicleId)
ORDER BY x.v;
-- Xe vượt định mức của hộ
INSERT INTO #A (Category, TypeId, Plate, CustomerId, InUtc, Note)
SELECT TOP (2) 1, x.TypeId, x.Plate, x.CustomerId, DATEADD(HOUR, -26, @Now), N'Xe vượt định mức'
FROM @Veh x JOIN @OverQuota q ON q.VehicleId = x.VehicleId
WHERE NOT EXISTS (SELECT 1 FROM @Tk t WHERE t.VehicleId = x.VehicleId)
ORDER BY x.v;
-- Vé tháng còn hạn
INSERT INTO #A (Category, TypeId, Plate, CustomerId, TicketCode, InUtc, Note)
SELECT TOP (5) 2, t.VehTypeId, t.Plate, t.CustomerId, t.Code, DATEADD(MINUTE, -(60 + t.t * 41 % 500), @Now), N'Vé tháng'
FROM @Tk t
WHERE t.Kind = N'Active' AND NOT EXISTS (SELECT 1 FROM @BlockedPlates b WHERE b.Norm = dbo.fn_NormalizePlate(t.Plate))
  AND NOT EXISTS (SELECT 1 FROM #A a WHERE a.Plate = t.Plate)
ORDER BY t.t;
-- Vé tháng hết hạn trong lúc xe đang trong bãi (vào trước ngày hết hạn 1 ngày)
INSERT INTO #A (Category, TypeId, Plate, CustomerId, TicketCode, InUtc, Note)
SELECT TOP (1) 2, t.VehTypeId, t.Plate, t.CustomerId, t.Code, DATEADD(DAY, -1, t.EndDate), N'Vé hết hạn khi xe trong bãi'
FROM @Tk t WHERE t.Kind = N'ExpiredActive' AND NOT EXISTS (SELECT 1 FROM #A a WHERE a.Plate = t.Plate)
ORDER BY t.EndDate DESC;

-- Vãng lai: 20 xe máy, 4 ô tô, 3 xe đạp/điện; 3 xe gửi quá hạn
;WITH N AS (SELECT TOP (27) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
INSERT INTO #A (Category, TypeId, Plate, InUtc, Note)
SELECT 0, x.TypeId,
       CASE x.TypeId
           WHEN @TCar THEN p.v + sc.v + N'-' + LEFT(dd.d5, 3) + N'.' + RIGHT(dd.d5, 2)
           WHEN @TBike THEN p.v + N'E3-' + LEFT(dd.d5, 3) + N'.' + RIGHT(dd.d5, 2)
           ELSE p.v + sm.v + N'-' + LEFT(dd.d5, 3) + N'.' + RIGHT(dd.d5, 2) END,
       CASE WHEN N.n IN (3, 11) THEN DATEADD(HOUR, -(60 + N.n * 4), @Now)
            WHEN N.n = 21 THEN DATEADD(DAY, -6, @Now)
            ELSE DATEADD(MINUTE, -(5 + N.n * 53 % 720), @Now) END,
       CASE WHEN N.n IN (3, 11, 21) THEN N'Vãng lai gửi quá hạn' WHEN N.n = 1 THEN N'Vừa vào (trong ân hạn)' ELSE N'Vãng lai' END
FROM N
CROSS APPLY (SELECT CASE WHEN N.n <= 20 THEN @TMoto WHEN N.n <= 24 THEN @TCar ELSE @TBike END AS TypeId) x
CROSS APPLY (SELECT CAST(10000 + ((5000 + N.n) * 373) % 90000 AS nvarchar(5)) AS d5) dd
JOIN @Prov p ON p.i = N.n % 9
JOIN @SerMoto sm ON sm.i = N.n % 8
JOIN @SerCar sc ON sc.i = N.n % 5;
UPDATE #A SET InUtc = DATEADD(MINUTE, -5, @Now) WHERE Note = N'Vừa vào (trong ân hạn)';

-- bỏ biển số đang có phiên Active thật trong database
DELETE a FROM #A a WHERE EXISTS (SELECT 1 FROM ParkingSessions ps WHERE ps.Status = 0
                                 AND dbo.fn_NormalizePlate(ps.LicensePlate) = dbo.fn_NormalizePlate(a.Plate));

-- Cấp ô theo đúng chính sách: cư dân ưu tiên ResidentOnly > Mixed > VisitorOnly; khách: VisitorOnly > Mixed
DECLARE @aid int, @cat int, @tid int, @slot int;
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT id, Category, TypeId FROM #A ORDER BY id;
OPEN cur;
FETCH NEXT FROM cur INTO @aid, @cat, @tid;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @slot = NULL;
    SELECT TOP (1) @slot = sl.SlotId
    FROM ParkingSlots sl JOIN ParkingZones z ON z.ZoneId = sl.ZoneId
    WHERE sl.VehicleTypeId = @tid AND sl.Status = 0
      AND NOT EXISTS (SELECT 1 FROM ParkingSessions ps WHERE ps.SlotId = sl.SlotId AND ps.Status = 0)
      AND NOT EXISTS (SELECT 1 FROM #A a WHERE a.SlotId = sl.SlotId)
      AND (@cat = 1 OR z.Audience IN (0, 2))
    ORDER BY CASE WHEN @cat = 1 THEN CASE z.Audience WHEN 1 THEN 0 WHEN 0 THEN 1 ELSE 2 END
                  ELSE CASE z.Audience WHEN 2 THEN 0 ELSE 1 END END, sl.SlotCode;
    UPDATE #A SET SlotId = @slot WHERE id = @aid;
    FETCH NEXT FROM cur INTO @aid, @cat, @tid;
END
CLOSE cur; DEALLOCATE cur;
DELETE FROM #A WHERE SlotId IS NULL;

UPDATE #A SET TicketCode = N'TMP-A' + CAST(id AS nvarchar(10))   -- đánh số VL-yyMMdd-0001 ở cuối script
WHERE TicketCode IS NULL;

DECLARE @ActMap TABLE (SessionId int, id int);
MERGE INTO ParkingSessions AS t
USING (SELECT a.*, (SELECT TOP 1 o.UserId FROM @Ops o ORDER BY (a.id % 4 + o.Idx) % 4) AS UserId FROM #A a) AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (TicketCode, LicensePlate, VehicleTypeId, SlotId, CheckInTime, TotalFee, Status, PaymentMethod, CustomerId, IsMonthlyPass, CustomerType, CreatedByUserId)
    VALUES (s.TicketCode, s.Plate, s.TypeId, s.SlotId, s.InUtc, 0, 0, 0, s.CustomerId,
            CASE WHEN s.Category = 2 THEN 1 ELSE 0 END, CASE WHEN s.Category = 1 THEN 0 ELSE 1 END, s.UserId)
OUTPUT inserted.SessionId, s.id INTO @ActMap (SessionId, id);
UPDATE a SET a.SessionId = m.SessionId FROM #A a JOIN @ActMap m ON m.id = a.id;

UPDATE sl SET sl.Status = 1, sl.CurrentLicensePlate = a.Plate
FROM ParkingSlots sl JOIN #A a ON a.SlotId = sl.SlotId;

-- Thanh toán VietQR đang chờ trong ca đang mở (test: không cho đóng ca khi còn QR chờ)
IF @OpenShiftId IS NOT NULL
BEGIN
    DECLARE @PendSession int = (SELECT TOP 1 SessionId FROM #A WHERE Category = 0 AND TypeId = @TCar ORDER BY id);
    IF @PendSession IS NOT NULL
    BEGIN
        INSERT INTO Payments (SessionId, ShiftId, CheckoutUserId, Amount, Currency, Status, PaymentMethod, Description, CreatedAt, UpdatedAt, ExpiredAt)
        VALUES (@PendSession, @OpenShiftId, @OpAn, 25000, N'VND', 1, 1, @Mark + N' Thanh toán QR đang chờ khách quét',
                DATEADD(MINUTE, -3, @Now), DATEADD(MINUTE, -3, @Now), DATEADD(MINUTE, 12, @Now));
        INSERT INTO PaymentTransactions (PaymentId, TransactionReference, GatewayOrderCode, Amount, Currency, Status, AccountNumber, AccountName, BankCode, CreatedAt, UpdatedAt)
        VALUES (SCOPE_IDENTITY(), N'SPTX-PENDING-' + CAST(@PendSession AS nvarchar(10)), CAST(79000000 + @PendSession AS nvarchar(20)),
                25000, N'VND', 1, N'0123456789', N'BAN QUAN LY BAI XE SMARTPS', N'MBBank', DATEADD(MINUTE, -3, @Now), DATEADD(MINUTE, -3, @Now));
    END
END

-- Mã vé lượt VL-yyMMdd-0001 theo thứ tự giờ vào trong ngày (giờ VN), nối tiếp các mã đã có
;WITH b AS (SELECT SUBSTRING(TicketCode, 4, 6) AS D, MAX(TRY_CAST(SUBSTRING(TicketCode, 11, 10) AS int)) AS M
            FROM ParkingSessions WHERE TicketCode LIKE N'VL-[0-9][0-9][0-9][0-9][0-9][0-9]-%' GROUP BY SUBSTRING(TicketCode, 4, 6)),
      n AS (SELECT SessionId, CONVERT(nvarchar(6), DATEADD(HOUR, 7, CheckInTime), 12) AS D,
                   ROW_NUMBER() OVER (PARTITION BY CAST(DATEADD(HOUR, 7, CheckInTime) AS date) ORDER BY CheckInTime, SessionId) AS rn
            FROM ParkingSessions WHERE TicketCode LIKE N'TMP-%')
UPDATE ps SET ps.TicketCode = N'VL-' + n.D + N'-' + RIGHT(N'0000' + CAST(ISNULL(b.M, 0) + n.rn AS nvarchar(10)), 4)
FROM ParkingSessions ps JOIN n ON n.SessionId = ps.SessionId LEFT JOIN b ON b.D = n.D;

-- ===================================================================================
-- 13. BIÊN BẢN SỰ CỐ
-- ===================================================================================
DECLARE @IncYear int = YEAR(DATEADD(HOUR, 7, @Now));
DECLARE @IncPrefix nvarchar(20) = N'SC-' + CAST(@IncYear AS nvarchar(4)) + N'-';
DECLARE @IncStart int = ISNULL((SELECT MAX(TRY_CAST(SUBSTRING(IncidentCode, LEN(@IncPrefix) + 1, 10) AS int))
                                FROM Incidents WHERE IncidentCode LIKE @IncPrefix + N'%'), 0);

;WITH I AS (
    SELECT * FROM (VALUES
        (1, 0, N'Khách làm mất vé lượt, đã xác minh giấy tờ xe và thu phí phạt', 1, 28),
        (2, 0, N'Khách mất thẻ vé tháng, đề nghị khoá thẻ', 1, 21),
        (3, 0, N'Khách báo mất vé, đang chờ xác minh cà vẹt', 0, 1),
        (4, 1, N'Biển số bị bùn che, camera không đọc được, nhân viên nhập tay', 1, 18),
        (5, 1, N'Biển số mờ, OCR đọc sai 2 ký tự', 1, 9),
        (6, 1, N'Xe không gắn biển số trước, yêu cầu khách xuất trình giấy tờ', 0, 0),
        (7, 2, N'Xe gửi quá 48 giờ chưa lấy, đã liên hệ chủ xe', 1, 14),
        (8, 2, N'Xe vãng lai gửi quá hạn, chưa liên hệ được chủ xe', 0, 2),
        (9, 2, N'Xe gửi quá hạn nhiều ngày, đề nghị lập biên bản tạm giữ', 0, 0),
        (10, 3, N'Xe máy bị trầy yên do va quẹt trong bãi', 1, 25),
        (11, 3, N'Ô tô bị móp cản sau khi lùi xe ở khu B', 0, 3),
        (12, 3, N'Khách phản ánh mất mũ bảo hiểm', 1, 11),
        (13, 4, N'Người lạ đi bộ vào hầm B2 lúc nửa đêm', 1, 16),
        (14, 4, N'Phát hiện xe trong danh sách cấm cố tình vào bãi', 1, 6),
        (15, 4, N'Camera ghi nhận người lục cốp xe khu A', 0, 1),
        (16, 5, N'Barrier làn vào không mở, phải mở tay 15 phút', 1, 19),
        (17, 5, N'Camera làn ra mất tín hiệu', 1, 7),
        (18, 5, N'Máy in vé hết giấy', 1, 4),
        (19, 5, N'Đèn chiếu sáng khu E bị hỏng', 0, 2),
        (20, 6, N'Khách tranh cãi về số tiền phải trả', 1, 12),
        (21, 6, N'Mất điện toàn bãi 20 phút, chuyển sang ghi tay', 1, 27),
        (22, 6, N'Khách để quên chìa khoá trên xe', 1, 5),
        (23, 6, N'Nước ngập nhẹ khu D sau mưa lớn', 0, 0),
        (24, 1, N'Biển số xe điện không đúng định dạng', 1, 10)
    ) AS v(n, Type, Descr, Resolved, DaysAgo))
INSERT INTO Incidents (IncidentCode, Type, LicensePlate, Description, Status, ReportedByUserId, ReportedAt, ResolvedByUserId, ResolvedAt, ResolutionNotes)
SELECT @IncPrefix + RIGHT(N'000' + CAST(@IncStart + ROW_NUMBER() OVER (ORDER BY I.DaysAgo DESC, I.n) AS nvarchar(6)), 3),
       I.Type,
       CASE WHEN I.Type = 2 AND I.Resolved = 0 THEN (SELECT TOP 1 a.Plate FROM #A a WHERE a.Note LIKE N'%quá hạn%' ORDER BY ABS(CHECKSUM(a.id * I.n)))
            WHEN I.Type IN (0, 1, 2, 3) THEN (SELECT TOP 1 s.Plate FROM #S s WHERE s.id % 50 = I.n)
            WHEN I.n = 14 THEN N'51G-666.88' END,
       @Mark + N' ' + I.Descr, I.Resolved,
       (SELECT TOP 1 o.UserId FROM @Ops o WHERE o.Idx = I.n % 4),
       DATEADD(MINUTE, -(I.n * 37 % 600), DATEADD(DAY, -I.DaysAgo, @Now)),
       CASE WHEN I.Resolved = 1 THEN CASE WHEN I.n % 2 = 0 THEN @MgrHoa ELSE @MgrKhanh END END,
       CASE WHEN I.Resolved = 1 THEN DATEADD(HOUR, 3 + I.n % 20, DATEADD(MINUTE, -(I.n * 37 % 600), DATEADD(DAY, -I.DaysAgo, @Now))) END,
       CASE WHEN I.Resolved = 1 THEN N'Đã xử lý xong, lưu hồ sơ' END
FROM I;
UPDATE Incidents SET ResolvedAt = @Now WHERE Description LIKE N'[[]MẪU]%' AND ResolvedAt > @Now;

COMMIT TRANSACTION;

-- ===================================================================================
-- TÓM TẮT
-- ===================================================================================
DECLARE @SU TABLE (UserId int PRIMARY KEY);
INSERT INTO @SU SELECT UserId FROM Users WHERE Email LIKE N'%@sample.smartps';
SELECT N'Hộ dân' AS [Loại], COUNT(*) AS [Số lượng] FROM Households WHERE Notes LIKE N'[[]MẪU]%'
UNION ALL SELECT N'Khách hàng', COUNT(*) FROM Customers WHERE Notes LIKE N'[[]MẪU]%'
UNION ALL SELECT N'Phương tiện', COUNT(*) FROM Vehicles v JOIN Customers c ON c.CustomerId = v.OwnerCustomerId WHERE c.Notes LIKE N'[[]MẪU]%'
UNION ALL SELECT N'Vé tháng', COUNT(*) FROM MonthlyTickets t JOIN Customers c ON c.CustomerId = t.CustomerId WHERE c.Notes LIKE N'[[]MẪU]%'
UNION ALL SELECT N'Lịch sử vé tháng', COUNT(*) FROM MonthlyTicketHistories h JOIN MonthlyTickets t ON t.TicketId = h.TicketId JOIN Customers c ON c.CustomerId = t.CustomerId WHERE c.Notes LIKE N'[[]MẪU]%'
UNION ALL SELECT N'Danh sách cấm', COUNT(*) FROM BlacklistedVehicles WHERE Notes LIKE N'[[]MẪU]%'
UNION ALL SELECT N'Ca trực', COUNT(*) FROM Shifts WHERE OpenedByUserId IN (SELECT UserId FROM @SU)
UNION ALL SELECT N'Lượt gửi đã xong', COUNT(*) FROM ParkingSessions WHERE CreatedByUserId IN (SELECT UserId FROM @SU) AND Status <> 0
UNION ALL SELECT N'Xe đang trong bãi', COUNT(*) FROM ParkingSessions WHERE CreatedByUserId IN (SELECT UserId FROM @SU) AND Status = 0
UNION ALL SELECT N'Giao dịch tài chính', COUNT(*) FROM FinancialTransactions WHERE ShiftId IN (SELECT ShiftId FROM Shifts WHERE OpenedByUserId IN (SELECT UserId FROM @SU))
UNION ALL SELECT N'Thanh toán VietQR', COUNT(*) FROM Payments WHERE Description LIKE N'[[]MẪU]%'
UNION ALL SELECT N'Sự cố', COUNT(*) FROM Incidents WHERE Description LIKE N'[[]MẪU]%';
