using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Storage;
using SmartPS.Services.Shifts;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartPS.Services.GateControl;

public class GateControlService : IGateControlService
{
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _auditService;
    private readonly IDbContextFactory<SmartPsDbContext>? _dbContextFactory;
    private readonly IParkingFeeCalculator _feeCalculator;
    private readonly IImageStorageService _imageStorageService;

    // Đường dẫn tệp lưu trữ ngoại tuyến và nhật ký kiểm toán
    private readonly string _offlineFilePath;
    private readonly string _auditLogPath;

    // Bộ nhớ đệm InMemory Fallback dự phòng khi DB chưa kết nối
    // Đồng bộ đa luồng bằng _syncLock để tránh race-conditions giữa 2 làn xe
    private readonly object _syncLock = new();
    private readonly List<ParkingSession> _memorySessions = new();
    private readonly List<VehicleType> _memoryVehicleTypes = new();
    private readonly List<ParkingSlot> _memorySlots = new();
    private readonly List<PricingRule> _memoryPricingRules = new();
    private readonly List<MonthlyTicket> _memoryMonthlyTickets = new();
    private int _sessionSequence = 1000;

    public GateControlService(
        IAuthorizationGuard guard,
        IAuditService auditService,
        IDbContextFactory<SmartPsDbContext>? dbContextFactory = null,
        IParkingFeeCalculator? feeCalculator = null,
        IImageStorageService? imageStorageService = null)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _dbContextFactory = dbContextFactory;
        _feeCalculator = feeCalculator ?? new StandardParkingFeeCalculator();
        _imageStorageService = imageStorageService ?? new ImageStorageService();

        var storageDir = Path.Combine(AppContext.BaseDirectory, "Storage");
        Directory.CreateDirectory(storageDir);
        _offlineFilePath = Path.Combine(storageDir, "offline_active_sessions.json");
        _auditLogPath = Path.Combine(storageDir, "gate_audit_log.jsonl");

        InitializeFallbackData();
        LoadOfflineSessions();
    }

    private void InitializeFallbackData()
    {
        _memoryVehicleTypes.AddRange(new[]
        {
            new VehicleType { VehicleTypeId = 1, TypeName = "Xe máy", Description = "Xe gắn máy hai bánh, xe tay ga, xe điện" },
            new VehicleType { VehicleTypeId = 2, TypeName = "Ô tô con", Description = "Xe du lịch từ 4 đến 7 chỗ ngồi" },
            new VehicleType { VehicleTypeId = 3, TypeName = "Xe tải / Xe khách", Description = "Xe trọng tải lớn, xe du lịch trên 16 chỗ" }
        });

        _memoryPricingRules.AddRange(new[]
        {
            new PricingRule
            {
                RuleId = 1, VehicleTypeId = 1, Block4hPrice = 5000,
                DailyPrice = 25000, Monthly1Price = 100000, Description = "Biểu phí xe máy tiêu chuẩn"
            },
            new PricingRule
            {
                RuleId = 2, VehicleTypeId = 2, Block4hPrice = 25000,
                DailyPrice = 100000, Monthly1Price = 1200000, Description = "Biểu phí ô tô con tiêu chuẩn"
            },
            new PricingRule
            {
                RuleId = 3, VehicleTypeId = 3, Block4hPrice = 2000,
                DailyPrice = 10000, Monthly1Price = 50000, Description = "Biểu phí xe tải tiêu chuẩn"
            }
        });

        for (int i = 1; i <= 20; i++)
        {
            _memorySlots.Add(new ParkingSlot
            {
                SlotId = i,
                SlotCode = i <= 10 ? $"A-{i:D2}" : $"B-{(i - 10):D2}",
                ZoneName = i <= 10 ? "Khu A - Xe Máy" : "Khu B - Ô Tô",
                VehicleTypeId = i <= 10 ? 1 : 2,
                Status = SlotStatus.Available
            });
        }
    }

    private static string NormalizePlate(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return string.Empty;
        return Regex.Replace(plate, @"[^a-zA-Z0-9]", "").ToUpperInvariant();
    }

    private void SaveOfflineSessions()
    {
        try
        {
            var active = _memorySessions.Where(s => s.Status == SessionStatus.Active).Select(s => new OfflineSessionDto
            {
                SessionId = s.SessionId,
                TicketCode = s.TicketCode,
                LicensePlate = s.LicensePlate,
                VehicleTypeId = s.VehicleTypeId,
                VehicleTypeName = s.VehicleType?.TypeName,
                SlotId = s.SlotId,
                SlotCode = s.Slot?.SlotCode,
                CheckInTime = s.CheckInTime,
                CheckInImagePath = s.CheckInImagePath,
                Status = s.Status,
                IsMonthlyPass = s.IsMonthlyPass,
                CustomerId = s.CustomerId,
                CustomerName = s.Customer?.FullName,
                CustomerType = s.CustomerType,
                CreatedByUserId = s.CreatedByUserId,
                TotalFee = s.TotalFee
            }).ToList();

            var json = JsonSerializer.Serialize(active, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_offlineFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GateControlService SaveOffline Error]: {ex.Message}");
        }
    }

    private void LoadOfflineSessions()
    {
        try
        {
            if (!File.Exists(_offlineFilePath)) return;
            var json = File.ReadAllText(_offlineFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var list = JsonSerializer.Deserialize<List<OfflineSessionDto>>(json);
            if (list == null || !list.Any()) return;

            lock (_syncLock)
            {
                foreach (var item in list)
                {
                    if (_memorySessions.Any(s => s.SessionId == item.SessionId || NormalizePlate(s.LicensePlate) == NormalizePlate(item.LicensePlate)))
                        continue;

                    var vType = _memoryVehicleTypes.FirstOrDefault(v => v.VehicleTypeId == item.VehicleTypeId);
                    var slot = item.SlotId.HasValue ? _memorySlots.FirstOrDefault(s => s.SlotId == item.SlotId.Value) : null;
                    if (slot != null)
                    {
                        slot.Status = SlotStatus.Occupied;
                        slot.CurrentLicensePlate = item.LicensePlate;
                    }

                    _memorySessions.Add(new ParkingSession
                    {
                        SessionId = item.SessionId,
                        TicketCode = item.TicketCode,
                        LicensePlate = item.LicensePlate,
                        VehicleTypeId = item.VehicleTypeId,
                        VehicleType = vType,
                        SlotId = item.SlotId,
                        Slot = slot,
                        CheckInTime = item.CheckInTime,
                        CheckInImagePath = item.CheckInImagePath,
                        Status = item.Status,
                        IsMonthlyPass = item.IsMonthlyPass,
                        CustomerId = item.CustomerId,
                        Customer = !string.IsNullOrEmpty(item.CustomerName) ? new Customer { CustomerId = item.CustomerId ?? 0, FullName = item.CustomerName, Type = (item.CustomerType == 0 && !item.IsMonthlyPass && item.CustomerId == null) ? CustomerType.External : item.CustomerType } : null,
                        CustomerType = (item.CustomerType == 0 && !item.IsMonthlyPass && item.CustomerId == null) ? CustomerType.External : item.CustomerType,
                        CreatedByUserId = item.CreatedByUserId,
                        TotalFee = item.TotalFee
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GateControlService LoadOffline Error]: {ex.Message}");
        }
    }

    private void AppendAuditLog(string action, ParkingSession session, decimal? fee = null, string? payment = null, bool dbAuditWritten = true)
    {
        try
        {
            var auditEntry = new
            {
                Timestamp = DateTime.UtcNow,
                Action = action,
                SessionId = session.SessionId,
                TicketCode = session.TicketCode,
                LicensePlate = session.LicensePlate,
                VehicleType = session.VehicleType?.TypeName,
                SlotCode = session.Slot?.SlotCode,
                CheckInTime = session.CheckInTime,
                CheckOutTime = session.CheckOutTime,
                DurationMinutes = (session.CheckOutTime.HasValue ? session.CheckOutTime.Value - session.CheckInTime : DateTime.UtcNow - session.CheckInTime).TotalMinutes,
                TotalFee = fee ?? session.TotalFee,
                PaymentMethod = payment ?? session.PaymentMethod.ToString(),
                IsMonthlyPass = session.IsMonthlyPass,
                CustomerName = session.Customer?.FullName,
                CheckInImage = session.CheckInImagePath,
                CheckOutImage = session.CheckOutImagePath,
                DbAudit = dbAuditWritten
            };

            var line = JsonSerializer.Serialize(auditEntry);
            File.AppendAllLines(_auditLogPath, new[] { line });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GateControlService Audit Error]: {ex.Message}");
        }
    }

    public Task<List<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken = default)
        => GetVehicleTypesAsync(cancellationToken, null);

    private async Task<List<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken, DbProbe? probe)
    {
        if (_dbContextFactory != null && probe?.Failed != true)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var list = await db.VehicleTypes.AsNoTracking().ToListAsync(cancellationToken);
                if (list.Any()) return list;
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); }
        }
        lock (_syncLock)
        {
            return _memoryVehicleTypes.ToList();
        }
    }

    public Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, CancellationToken cancellationToken = default)
        => SuggestAvailableSlotAsync(vehicleTypeId, cancellationToken, null);

    private async Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, CancellationToken cancellationToken, DbProbe? probe)
    {
        if (_dbContextFactory != null && probe?.Failed != true)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var slot = await db.ParkingSlots
                    .AsNoTracking()
                    .Where(s => s.VehicleTypeId == vehicleTypeId && s.Status == SlotStatus.Available)
                    .OrderBy(s => s.SlotCode)
                    .FirstOrDefaultAsync(cancellationToken);

                if (slot != null) return slot;
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); }
        }

        lock (_syncLock)
        {
            return _memorySlots.FirstOrDefault(s => s.VehicleTypeId == vehicleTypeId && s.Status == SlotStatus.Available);
        }
    }

    public Task<MonthlyTicket?> FindActiveMonthlyTicketAsync(string licensePlate, CancellationToken cancellationToken = default)
        => FindActiveMonthlyTicketAsync(licensePlate, cancellationToken, null);

    private async Task<MonthlyTicket?> FindActiveMonthlyTicketAsync(string licensePlate, CancellationToken cancellationToken, DbProbe? probe)
    {
        var norm = NormalizePlate(licensePlate);
        if (string.IsNullOrEmpty(norm)) return null;

        if (_dbContextFactory != null && probe?.Failed != true)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var now = DateTime.UtcNow;
                var tickets = await db.MonthlyTickets
                    .Include(t => t.Customer)
                    .Include(t => t.VehicleType)
                    .Where(t => t.Status == MonthlyTicketStatus.Active && t.StartDate <= now && t.EndDate >= now)
                    .ToListAsync(cancellationToken);

                var matched = tickets.FirstOrDefault(t => NormalizePlate(t.RegisteredLicensePlate) == norm);
                if (matched != null) return matched;
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); }
        }

        lock (_syncLock)
        {
            return _memoryMonthlyTickets.FirstOrDefault(t => NormalizePlate(t.RegisteredLicensePlate) == norm && t.IsCurrentlyValid);
        }
    }

    /// <summary>Remembers that a database call of the current request failed, so the request can skip further database work.</summary>
    private sealed class DbProbe
    {
        public bool Failed { get; private set; }

        public void MarkFailed() => Failed = true;
    }

    public async Task<GateCheckInResult> ProcessCheckInAsync(GateCheckInRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.CreatedByUserId.HasValue)
            {
                await _guard.DemandActorAsync(request.CreatedByUserId.Value, Permissions.ParkingCheckIn, "ParkingSession", null, cancellationToken);
            }
            else
            {
                await _guard.DemandAsync(Permissions.ParkingCheckIn, "ParkingSession", null, cancellationToken);
            }
        }
        catch (PermissionDeniedException ex)
        {
            return new GateCheckInResult { Success = false, IsPermissionDenied = true, Message = ex.Message };
        }

        var createdByUserId = request.CreatedByUserId ?? _guard.CurrentUserId;

        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return new GateCheckInResult { Success = false, Message = "Biển số xe không được để trống." };
        }

        var cleanPlate = request.LicensePlate.Trim().ToUpperInvariant();
        var normPlate = NormalizePlate(cleanPlate);

        // Kiểm tra xem xe này có đang trong bãi hay chưa (phiên Active trùng biển số)
        var probe = new DbProbe();
        var activeSessions = await GetActiveSessionsAsync(cancellationToken, probe);
        if (activeSessions.Any(s => NormalizePlate(s.LicensePlate) == normPlate))
        {
            return new GateCheckInResult
            {
                Success = false,
                Message = $"Biển số '{cleanPlate}' hiện đang có phiên gửi xe chưa xuất bãi!"
            };
        }

        // Kiểm tra vé tháng
        var monthlyTicket = await FindActiveMonthlyTicketAsync(cleanPlate, cancellationToken, probe);
        var isMonthly = monthlyTicket != null;
        var vehicleTypeId = request.VehicleTypeId > 0 ? request.VehicleTypeId : (monthlyTicket?.VehicleTypeId ?? 1);

        // Tìm ô đỗ phù hợp
        ParkingSlot? assignedSlot = null;
        if (request.SlotId.HasValue && request.SlotId.Value > 0)
        {
            // Được chỉ định slot cụ thể
        }
        else
        {
            assignedSlot = await SuggestAvailableSlotAsync(vehicleTypeId, cancellationToken, probe);
        }

        var ticketCode = isMonthly 
            ? monthlyTicket!.TicketCode 
            : $"TK-{DateTime.UtcNow:yyyyMMdd}-{Interlocked.Increment(ref _sessionSequence)}";

        // Lưu trữ an toàn vĩnh viễn tệp ảnh chụp khi xe vào
        var archivedImagePath = await _imageStorageService.ArchiveCaptureAsync(request.ImagePath, "CheckIn", cleanPlate);

        var vTypes = await GetVehicleTypesAsync(cancellationToken, probe);
        var matchedVType = vTypes.FirstOrDefault(v => v.VehicleTypeId == vehicleTypeId);

        var session = new ParkingSession
        {
            TicketCode = ticketCode,
            LicensePlate = cleanPlate,
            VehicleTypeId = vehicleTypeId,
            VehicleType = matchedVType,
            SlotId = assignedSlot?.SlotId,
            Slot = assignedSlot,
            CheckInTime = DateTime.UtcNow,
            CheckInImagePath = archivedImagePath ?? request.ImagePath,
            Status = SessionStatus.Active,
            IsMonthlyPass = isMonthly,
            CustomerId = monthlyTicket?.CustomerId,
            Customer = monthlyTicket?.Customer,
            CustomerType = isMonthly ? CustomerType.Resident : CustomerType.External,
            CreatedByUserId = createdByUserId,
            TotalFee = 0
        };

        var dbAuditWritten = false;
        if (_dbContextFactory != null && !probe.Failed)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                await using (var transaction = await _auditService.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
                {
                    var dbSession = new ParkingSession
                    {
                        TicketCode = session.TicketCode,
                        LicensePlate = session.LicensePlate,
                        VehicleTypeId = session.VehicleTypeId,
                        SlotId = session.SlotId,
                        CheckInTime = session.CheckInTime,
                        CheckInImagePath = session.CheckInImagePath,
                        Status = session.Status,
                        IsMonthlyPass = session.IsMonthlyPass,
                        CustomerId = session.CustomerId,
                        CustomerType = session.CustomerType,
                        CreatedByUserId = session.CreatedByUserId,
                        TotalFee = 0
                    };

                    db.ParkingSessions.Add(dbSession);

                    if (assignedSlot != null)
                    {
                        var dbSlot = await db.ParkingSlots.FindAsync(new object[] { assignedSlot.SlotId }, cancellationToken);
                        if (dbSlot != null)
                        {
                            dbSlot.Status = SlotStatus.Occupied;
                            dbSlot.CurrentLicensePlate = cleanPlate;
                        }
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    await _auditService.AppendAsync(db, new AuditEntry(
                        AuditActions.ParkingCheckIn,
                        AuditOutcome.Success,
                        "ParkingSession",
                        dbSession.SessionId.ToString(),
                        new
                        {
                            LicensePlate = cleanPlate,
                            TicketCode = session.TicketCode,
                            VehicleTypeId = vehicleTypeId,
                            SlotCode = assignedSlot?.SlotCode,
                            IsMonthlyPass = isMonthly
                        }), cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    session.SessionId = dbSession.SessionId;
                    dbAuditWritten = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (DbConnectionHelper.IsConnectionException(ex))
            {
                // Mất kết nối cơ sở dữ liệu: tiếp tục ở chế độ ngoại tuyến (bộ nhớ + JSONL)
                System.Diagnostics.Debug.WriteLine($"[GateControlService DB Error]: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Lỗi khác (ghi nhật ký thất bại, hết thời gian chờ khóa nhật ký...): không lưu ngoại tuyến
                System.Diagnostics.Debug.WriteLine($"[GateControlService Audit Error]: {ex.GetType().Name}");
                return new GateCheckInResult
                {
                    Success = false,
                    Message = "Không thể ghi nhận lượt vào bãi vì lỗi ghi nhật ký kiểm toán. Vui lòng thử lại."
                };
            }
        }

        // Lưu bộ nhớ đệm và tệp ngoại tuyến (đồng bộ luồng)
        lock (_syncLock)
        {
            if (session.SessionId <= 0)
            {
                session.SessionId = Interlocked.Increment(ref _sessionSequence);
            }
            if (assignedSlot != null)
            {
                var memSlot = _memorySlots.FirstOrDefault(s => s.SlotId == assignedSlot.SlotId);
                if (memSlot != null)
                {
                    memSlot.Status = SlotStatus.Occupied;
                    memSlot.CurrentLicensePlate = cleanPlate;
                }
            }
            _memorySessions.RemoveAll(s => s.SessionId == session.SessionId || NormalizePlate(s.LicensePlate) == normPlate);
            _memorySessions.Insert(0, session);
            SaveOfflineSessions();
            AppendAuditLog("CHECK_IN", session, dbAuditWritten: dbAuditWritten);
        }

        return new GateCheckInResult
        {
            Success = true,
            Message = isMonthly ? "Xe vé tháng vào bãi thành công." : "Xe vãng lai vào bãi thành công.",
            Session = session,
            IsMonthlyTicket = isMonthly,
            CustomerName = monthlyTicket?.Customer?.FullName,
            AssignedSlotCode = assignedSlot?.SlotCode
        };
    }

    public async Task<GateCheckOutCalculationResult> CalculateCheckOutAsync(string licensePlate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licensePlate))
        {
            return new GateCheckOutCalculationResult { Success = false, Message = "Vui lòng nhập hoặc nhận diện biển số xe cần xuất bãi." };
        }

        var norm = NormalizePlate(licensePlate);
        var activeSessions = await GetActiveSessionsAsync(cancellationToken);
        var session = activeSessions.FirstOrDefault(s => NormalizePlate(s.LicensePlate) == norm);

        if (session == null)
        {
            return new GateCheckOutCalculationResult
            {
                Success = false,
                Message = $"Không tìm thấy phiên gửi xe đang hoạt động cho biển số '{licensePlate}'!"
            };
        }

        var now = DateTime.UtcNow;
        var pricingRule = await GetPricingRuleAsync(session.VehicleTypeId, cancellationToken);
        var feeResult = _feeCalculator.CalculateFee(session, pricingRule, now);

        return new GateCheckOutCalculationResult
        {
            Success = true,
            ActiveSession = session,
            CheckOutTime = now,
            Duration = feeResult.Duration,
            RawFee = feeResult.RawFee,
            TotalFee = feeResult.TotalFee,
            IsMonthlyTicket = feeResult.IsMonthlyTicket,
            CustomerName = session.Customer?.FullName,
            Message = feeResult.Message,
            FeeDetails = feeResult.FeeDetails
        };
    }

    public async Task<GateCheckOutResult> CompleteCheckOutAsync(GateCheckOutRequest request, CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory == null || request.ActorUserId <= 0)
            return new GateCheckOutResult { Success = false, Message = "Cần đăng nhập và kết nối cơ sở dữ liệu để thu tiền." };

        try
        {
            await _guard.DemandActorAsync(request.ActorUserId, Permissions.ParkingCheckOut, "ParkingSession", request.SessionId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return new GateCheckOutResult { Success = false, IsPermissionDenied = true, Message = ex.Message };
        }

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Xử lý tệp ảnh trước khi mở giao dịch có khoá nhật ký (không I/O khi đang giữ khoá)
            var licensePlate = await db.ParkingSessions
                .AsNoTracking()
                .Where(s => s.SessionId == request.SessionId)
                .Select(s => s.LicensePlate)
                .FirstOrDefaultAsync(cancellationToken);
            var archivedOutImage = licensePlate == null
                ? null
                : await _imageStorageService.ArchiveCaptureAsync(request.CheckOutImagePath, "CheckOut", licensePlate);

            ParkingSession completedSession;
            PaymentMethod completedMethod;
            await using (var transaction = await _auditService.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.Serializable, cancellationToken))
            {
                var shift = await ShiftAccounting.FindActiveShiftAsync(db, request.ActorUserId, cancellationToken);
                if (shift == null)
                    return new GateCheckOutResult { Success = false, Message = "Bạn cần mở ca trực trước khi checkout." };

                var session = await db.ParkingSessions.Include(s => s.Slot)
                    .FirstOrDefaultAsync(s => s.SessionId == request.SessionId, cancellationToken);
                if (session == null || session.Status != SessionStatus.Active)
                    return new GateCheckOutResult { Success = false, Message = "Phiên gửi xe không còn hoạt động." };

                var method = request.TotalFee <= 0 ? PaymentMethod.Free : request.PaymentMethod;
                if (request.TotalFee < 0 || (request.TotalFee > 0 && method == PaymentMethod.Free))
                    return new GateCheckOutResult { Success = false, Message = "Số tiền hoặc phương thức thanh toán không hợp lệ." };
                if (request.TotalFee > 0 && method == PaymentMethod.VietQR)
                    return new GateCheckOutResult { Success = false, Message = "Thanh toán VietQR phải được xác nhận qua cổng thanh toán." };

                await ShiftAccounting.AddParkingFeeAsync(db, shift, request.ActorUserId,
                    session.SessionId, method, request.TotalFee, null, cancellationToken);
                session.CheckOutTime = DateTime.UtcNow;
                session.CheckOutImagePath = archivedOutImage ?? request.CheckOutImagePath;
                session.TotalFee = request.TotalFee;
                session.PaymentMethod = method;
                session.Status = SessionStatus.Completed;
                if (session.Slot != null)
                {
                    session.Slot.Status = SlotStatus.Available;
                    session.Slot.CurrentLicensePlate = null;
                }

                await _auditService.AppendAsync(db, new AuditEntry(
                    AuditActions.ParkingCheckOut,
                    AuditOutcome.Success,
                    "ParkingSession",
                    session.SessionId.ToString(),
                    new
                    {
                        SessionId = session.SessionId,
                        LicensePlate = session.LicensePlate,
                        TicketCode = session.TicketCode,
                        Fee = request.TotalFee,
                        PaymentMethod = method.ToString(),
                        ShiftId = shift.ShiftId
                    }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                completedSession = session;
                completedMethod = method;
            }

            lock (_syncLock)
            {
                _memorySessions.RemoveAll(s => s.SessionId == completedSession.SessionId);
                if (completedSession.SlotId.HasValue)
                {
                    var memSlot = _memorySlots.FirstOrDefault(s => s.SlotId == completedSession.SlotId.Value);
                    if (memSlot != null)
                    {
                        memSlot.Status = SlotStatus.Available;
                        memSlot.CurrentLicensePlate = null;
                    }
                }
                SaveOfflineSessions();
                AppendAuditLog("CHECK_OUT", completedSession, request.TotalFee, completedMethod.ToString());
            }
            return new GateCheckOutResult { Success = true, Message = "Xuất bãi và thanh toán hoàn tất.", CompletedSession = completedSession };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GateControlService CompleteCheckOut DB Error]: {ex}");
            return new GateCheckOutResult { Success = false, Message = $"Không thể hoàn tất checkout: {ex.Message}" };
        }
    }

    public Task<List<ParkingSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
        => GetActiveSessionsAsync(cancellationToken, null);

    private async Task<List<ParkingSession>> GetActiveSessionsAsync(CancellationToken cancellationToken, DbProbe? probe)
    {
        if (_dbContextFactory != null && probe?.Failed != true)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var list = await db.ParkingSessions
                    .AsNoTracking()
                    .Include(s => s.VehicleType)
                    .Include(s => s.Slot)
                    .Include(s => s.Customer)
                    .Where(s => s.Status == SessionStatus.Active)
                    .OrderByDescending(s => s.CheckInTime)
                    .ToListAsync(cancellationToken);

                return list;
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); }
        }

        lock (_syncLock)
        {
            return _memorySessions
                .Where(s => s.Status == SessionStatus.Active)
                .OrderByDescending(s => s.CheckInTime)
                .ToList();
        }
    }

    private async Task<PricingRule?> GetPricingRuleAsync(int vehicleTypeId, CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory != null)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var rule = await db.PricingRules
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.VehicleTypeId == vehicleTypeId, cancellationToken);
                if (rule != null) return rule;
            }
            catch { /* Fallback */ }
        }

        lock (_syncLock)
        {
            return _memoryPricingRules.FirstOrDefault(r => r.VehicleTypeId == vehicleTypeId);
        }
    }

    public async Task<List<ParkingSlot>> GetAllSlotsAsync(CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory != null)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var list = await db.ParkingSlots
                    .AsNoTracking()
                    .Include(s => s.VehicleType)
                    .OrderBy(s => s.SlotCode)
                    .ToListAsync(cancellationToken);
                if (list.Any()) return list;
            }
            catch { /* Fallback */ }
        }

        lock (_syncLock)
        {
            return _memorySlots.ToList();
        }
    }

    public async Task<List<ParkingSession>> GetAllSessionsHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory != null)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var list = await db.ParkingSessions
                    .AsNoTracking()
                    .Include(s => s.VehicleType)
                    .Include(s => s.Slot)
                    .Include(s => s.Customer)
                    .OrderByDescending(s => s.CheckInTime)
                    .Take(100)
                    .ToListAsync(cancellationToken);
                if (list.Any()) return list;
            }
            catch { /* Fallback */ }
        }

        lock (_syncLock)
        {
            return _memorySessions.OrderByDescending(s => s.CheckInTime).ToList();
        }
    }

    public async Task<OverviewKpiData> GetOverviewKpiAsync(CancellationToken cancellationToken = default)
    {
        var slots = await GetAllSlotsAsync(cancellationToken);
        var activeSessions = await GetActiveSessionsAsync(cancellationToken);
        var allHistory = await GetAllSessionsHistoryAsync(cancellationToken);

        var totalSlots = slots.Count > 0 ? slots.Count : 20;
        var occupiedCount = activeSessions.Count;
        var availableSlots = Math.Max(0, totalSlots - occupiedCount);
        var occupancyRate = totalSlots > 0 ? Math.Round((double)occupiedCount / totalSlots * 100, 1) : 0;

        var today = DateTime.UtcNow.Date;
        var todayCheckIns = allHistory.Count(s => s.CheckInTime.Date == today);
        var todayCompleted = allHistory.Where(s => s.CheckOutTime.HasValue && s.CheckOutTime.Value.Date == today).ToList();
        var todayCheckOuts = todayCompleted.Count;
        var todayRevenue = todayCompleted.Sum(s => s.TotalFee);

        var motorbikeCount = activeSessions.Count(s => s.VehicleTypeId == 1);
        var carCount = activeSessions.Count(s => s.VehicleTypeId == 2);
        var monthlyCount = activeSessions.Count(s => s.IsMonthlyPass);
        var regularCount = activeSessions.Count(s => !s.IsMonthlyPass);

        return new OverviewKpiData
        {
            TotalParkedVehicles = occupiedCount,
            TotalSlots = totalSlots,
            AvailableSlots = availableSlots,
            OccupiedSlots = occupiedCount,
            OccupancyRate = occupancyRate,
            TodayCheckIns = Math.Max(occupiedCount, todayCheckIns),
            TodayCheckOuts = todayCheckOuts,
            TodayRevenue = todayRevenue,
            MotorbikeParkedCount = motorbikeCount,
            CarParkedCount = carCount,
            MonthlyParkedCount = monthlyCount,
            RegularParkedCount = regularCount
        };
    }

    public event EventHandler<ParkingSession>? SessionCompleted;

    public void NotifySessionCompleted(ParkingSession session)
    {
        if (session == null) return;

        lock (_syncLock)
        {
            _memorySessions.RemoveAll(s => s.SessionId == session.SessionId);
            if (session.SlotId.HasValue)
            {
                var memSlot = _memorySlots.FirstOrDefault(s => s.SlotId == session.SlotId.Value);
                if (memSlot != null)
                {
                    memSlot.Status = SlotStatus.Available;
                    memSlot.CurrentLicensePlate = null;
                }
            }
            SaveOfflineSessions();
            AppendAuditLog("CHECK_OUT_PAYMENT", session, session.TotalFee, session.PaymentMethod.ToString());
        }

        SessionCompleted?.Invoke(this, session);
    }
}

