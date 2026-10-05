using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.Storage;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartPS.Services.GateControl;

public class GateControlService : IGateControlService
{
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
        IDbContextFactory<SmartPsDbContext>? dbContextFactory = null,
        IParkingFeeCalculator? feeCalculator = null,
        IImageStorageService? imageStorageService = null)
    {
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
                RuleId = 1, VehicleTypeId = 1, FirstBlockMinutes = 120, FirstBlockPrice = 5000,
                AdditionalPricePerHour = 2000, OvernightPrice = 15000, Description = "Biểu phí xe máy tiêu chuẩn"
            },
            new PricingRule
            {
                RuleId = 2, VehicleTypeId = 2, FirstBlockMinutes = 120, FirstBlockPrice = 25000,
                AdditionalPricePerHour = 10000, OvernightPrice = 70000, Description = "Biểu phí ô tô con tiêu chuẩn"
            },
            new PricingRule
            {
                RuleId = 3, VehicleTypeId = 3, FirstBlockMinutes = 120, FirstBlockPrice = 40000,
                AdditionalPricePerHour = 15000, OvernightPrice = 120000, Description = "Biểu phí xe tải tiêu chuẩn"
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
                        Customer = !string.IsNullOrEmpty(item.CustomerName) ? new Customer { CustomerId = item.CustomerId ?? 0, FullName = item.CustomerName, Type = item.CustomerType } : null,
                        CustomerType = item.CustomerType,
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

    private void AppendAuditLog(string action, ParkingSession session, decimal? fee = null, string? payment = null)
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
                CheckOutImage = session.CheckOutImagePath
            };

            var line = JsonSerializer.Serialize(auditEntry);
            File.AppendAllLines(_auditLogPath, new[] { line });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GateControlService Audit Error]: {ex.Message}");
        }
    }

    public async Task<List<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory != null)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var list = await db.VehicleTypes.AsNoTracking().ToListAsync(cancellationToken);
                if (list.Any()) return list;
            }
            catch { /* Dùng fallback */ }
        }
        lock (_syncLock)
        {
            return _memoryVehicleTypes.ToList();
        }
    }

    public async Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory != null)
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
            catch { /* Dùng fallback */ }
        }

        lock (_syncLock)
        {
            return _memorySlots.FirstOrDefault(s => s.VehicleTypeId == vehicleTypeId && s.Status == SlotStatus.Available);
        }
    }

    public async Task<MonthlyTicket?> FindActiveMonthlyTicketAsync(string licensePlate, CancellationToken cancellationToken = default)
    {
        var norm = NormalizePlate(licensePlate);
        if (string.IsNullOrEmpty(norm)) return null;

        if (_dbContextFactory != null)
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
            catch { /* Dùng fallback */ }
        }

        lock (_syncLock)
        {
            return _memoryMonthlyTickets.FirstOrDefault(t => NormalizePlate(t.RegisteredLicensePlate) == norm && t.IsCurrentlyValid);
        }
    }

    public async Task<GateCheckInResult> ProcessCheckInAsync(GateCheckInRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return new GateCheckInResult { Success = false, Message = "Biển số xe không được để trống." };
        }

        var cleanPlate = request.LicensePlate.Trim().ToUpperInvariant();
        var normPlate = NormalizePlate(cleanPlate);

        // Kiểm tra xem xe này có đang trong bãi hay chưa (phiên Active trùng biển số)
        var activeSessions = await GetActiveSessionsAsync(cancellationToken);
        if (activeSessions.Any(s => NormalizePlate(s.LicensePlate) == normPlate))
        {
            return new GateCheckInResult
            {
                Success = false,
                Message = $"Biển số '{cleanPlate}' hiện đang có phiên gửi xe chưa xuất bãi!"
            };
        }

        // Kiểm tra vé tháng
        var monthlyTicket = await FindActiveMonthlyTicketAsync(cleanPlate, cancellationToken);
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
            assignedSlot = await SuggestAvailableSlotAsync(vehicleTypeId, cancellationToken);
        }

        var ticketCode = isMonthly 
            ? monthlyTicket!.TicketCode 
            : $"TK-{DateTime.UtcNow:yyyyMMdd}-{Interlocked.Increment(ref _sessionSequence)}";

        // Lưu trữ an toàn vĩnh viễn tệp ảnh chụp khi xe vào
        var archivedImagePath = await _imageStorageService.ArchiveCaptureAsync(request.ImagePath, "CheckIn", cleanPlate);

        var vTypes = await GetVehicleTypesAsync(cancellationToken);
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
            CustomerType = isMonthly ? CustomerType.VIP : CustomerType.Regular,
            CreatedByUserId = request.CreatedByUserId,
            TotalFee = 0
        };

        if (_dbContextFactory != null)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
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
                session.SessionId = dbSession.SessionId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GateControlService DB Error]: {ex.Message}");
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
            AppendAuditLog("CHECK_IN", session);
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
            DiscountPercentage = feeResult.DiscountPercentage,
            TotalFee = feeResult.TotalFee,
            IsMonthlyTicket = feeResult.IsMonthlyTicket,
            CustomerName = session.Customer?.FullName,
            Message = feeResult.Message
        };
    }

    public async Task<GateCheckOutResult> CompleteCheckOutAsync(GateCheckOutRequest request, CancellationToken cancellationToken = default)
    {
        ParkingSession? session = null;

        if (_dbContextFactory != null)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                session = await db.ParkingSessions
                    .Include(s => s.Slot)
                    .FirstOrDefaultAsync(s => s.SessionId == request.SessionId, cancellationToken);

                if (session != null)
                {
                    var archivedOutImage = await _imageStorageService.ArchiveCaptureAsync(request.CheckOutImagePath, "CheckOut", session.LicensePlate);

                    session.CheckOutTime = DateTime.UtcNow;
                    session.CheckOutImagePath = archivedOutImage ?? request.CheckOutImagePath;
                    session.TotalFee = request.TotalFee;
                    session.PaymentMethod = request.PaymentMethod;
                    session.Status = SessionStatus.Completed;

                    if (session.Slot != null)
                    {
                        session.Slot.Status = SlotStatus.Available;
                        session.Slot.CurrentLicensePlate = null;
                    }
                    else if (session.SlotId.HasValue)
                    {
                        var slot = await db.ParkingSlots.FindAsync(new object[] { session.SlotId.Value }, cancellationToken);
                        if (slot != null)
                        {
                            slot.Status = SlotStatus.Available;
                            slot.CurrentLicensePlate = null;
                        }
                    }

                    await db.SaveChangesAsync(cancellationToken);

                    // Đồng bộ bộ nhớ đệm và tệp ngoại tuyến
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
                        AppendAuditLog("CHECK_OUT", session, request.TotalFee, request.PaymentMethod.ToString());
                    }

                    return new GateCheckOutResult
                    {
                        Success = true,
                        Message = "Xuất bãi và thanh toán hoàn tất.",
                        CompletedSession = session
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GateControlService CompleteCheckOut DB Error]: {ex.Message}");
            }
        }

        // Fallback InMemory (đồng bộ luồng)
        lock (_syncLock)
        {
            session = _memorySessions.FirstOrDefault(s => s.SessionId == request.SessionId);
            if (session != null)
            {
                session.CheckOutTime = DateTime.UtcNow;
                session.CheckOutImagePath = request.CheckOutImagePath;
                session.TotalFee = request.TotalFee;
                session.PaymentMethod = request.PaymentMethod;
                session.Status = SessionStatus.Completed;

                if (session.SlotId.HasValue)
                {
                    var memSlot = _memorySlots.FirstOrDefault(s => s.SlotId == session.SlotId.Value);
                    if (memSlot != null)
                    {
                        memSlot.Status = SlotStatus.Available;
                        memSlot.CurrentLicensePlate = null;
                    }
                }

                _memorySessions.Remove(session);
                SaveOfflineSessions();
                AppendAuditLog("CHECK_OUT", session, request.TotalFee, request.PaymentMethod.ToString());

                return new GateCheckOutResult
                {
                    Success = true,
                    Message = "Xuất bãi và thanh toán hoàn tất.",
                    CompletedSession = session
                };
            }
        }

        return new GateCheckOutResult { Success = false, Message = "Không tìm thấy phiên gửi xe hợp lệ." };
    }

    public async Task<List<ParkingSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
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
                    .Where(s => s.Status == SessionStatus.Active)
                    .OrderByDescending(s => s.CheckInTime)
                    .ToListAsync(cancellationToken);

                return list;
            }
            catch { /* Fallback */ }
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
}

