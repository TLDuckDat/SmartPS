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
    private readonly IPlateOwnershipReader _plateOwnershipReader;

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
        IImageStorageService? imageStorageService = null,
        IPlateOwnershipReader? plateOwnershipReader = null)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _dbContextFactory = dbContextFactory;
        _feeCalculator = feeCalculator ?? new StandardParkingFeeCalculator();
        _imageStorageService = imageStorageService ?? new ImageStorageService();
        _plateOwnershipReader = plateOwnershipReader ?? new PlateOwnershipReader();

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
        => SuggestAvailableSlotAsync(vehicleTypeId, VehicleCategory.Visitor, cancellationToken, null);

    public Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, VehicleCategory category, CancellationToken cancellationToken = default)
        => SuggestAvailableSlotAsync(vehicleTypeId, category, cancellationToken, null);

    /// <summary>Gợi ý ô theo cùng quy tắc cấp ô khi check-in (theo đối tượng của khu), không khoá hay thay đổi trạng thái ô.</summary>
    private async Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, VehicleCategory category, CancellationToken cancellationToken, DbProbe? probe)
    {
        if (category == VehicleCategory.Blacklisted) return null;

        if (_dbContextFactory != null && probe?.Failed != true)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var candidates = await db.ParkingSlots
                    .AsNoTracking()
                    .Where(s => s.VehicleTypeId == vehicleTypeId
                                && s.Status == SlotStatus.Available
                                && !db.ParkingSessions.Any(p => p.SlotId == s.SlotId && p.Status == SessionStatus.Active))
                    .Select(s => new SlotCandidate(
                        s.SlotId,
                        s.SlotCode,
                        s.VehicleTypeId,
                        s.Status,
                        s.Zone == null ? ZoneAudience.Mixed : s.Zone.Audience,
                        s.ZoneId,
                        s.Zone == null ? null : s.Zone.ZoneCode,
                        false))
                    .ToListAsync(cancellationToken);

                var picked = SlotAllocationPolicy.PickSlot(candidates, category, vehicleTypeId);
                if (picked == null) return null;

                return await db.ParkingSlots
                    .AsNoTracking()
                    .Include(s => s.Zone)
                    .FirstOrDefaultAsync(s => s.SlotId == picked.SlotId, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); }
        }

        lock (_syncLock)
        {
            return _memorySlots.FirstOrDefault(s => s.VehicleTypeId == vehicleTypeId && s.Status == SlotStatus.Available);
        }
    }

    /// <summary>Phân loại xe theo biển số để hiển thị trước khi check-in/check-out; ngoại tuyến hoặc lỗi thì coi là khách vãng lai.</summary>
    public async Task<VehicleClassification> ClassifyVehicleAsync(string licensePlate, CancellationToken cancellationToken = default)
    {
        var norm = NormalizePlate(licensePlate);
        try
        {
            await _guard.DemandAnyAsync(new[] { Permissions.ParkingCheckIn, Permissions.ParkingCheckOut }, "ParkingSession", null, cancellationToken);
        }
        catch (PermissionDeniedException)
        {
            return VehicleClassification.Visitor(norm);
        }

        if (string.IsNullOrEmpty(norm) || _dbContextFactory == null) return VehicleClassification.Visitor(norm);

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var blacklist = await GateClassificationQueries.FindActiveBlacklistAsync(db, norm, cancellationToken);
            return await GateClassificationQueries.ClassifyAsync(db, _plateOwnershipReader, blacklist, norm, 0, DateTime.UtcNow, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return VehicleClassification.Visitor(norm);
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
                var ownership = await _plateOwnershipReader.ReadAsync(db, norm, now, cancellationToken);
                var honoured = VehicleClassifier.Classify(norm, null, ownership, 0, now).Ticket;
                if (honoured != null)
                {
                    var matched = await db.MonthlyTickets
                        .AsNoTracking()
                        .Include(t => t.Customer)
                        .Include(t => t.VehicleType)
                        .FirstOrDefaultAsync(t => t.TicketId == honoured.TicketId, cancellationToken);
                    if (matched != null) return matched;
                }

                return null;
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); }
        }

        lock (_syncLock)
        {
            return _memoryMonthlyTickets.FirstOrDefault(t => NormalizePlate(t.RegisteredLicensePlate) == norm && t.IsCurrentlyValid);
        }
    }

    public Task<Vehicle?> FindVehicleOwnerAsync(string licensePlate, CancellationToken cancellationToken = default)
        => FindVehicleOwnerAsync(licensePlate, cancellationToken, null);

    private async Task<Vehicle?> FindVehicleOwnerAsync(string licensePlate, CancellationToken cancellationToken, DbProbe? probe)
    {
        var norm = NormalizePlate(licensePlate);
        if (string.IsNullOrEmpty(norm)) return null;

        if (_dbContextFactory != null && probe?.Failed != true)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var vehicles = await db.Vehicles
                    .Include(v => v.OwnerCustomer)
                        .ThenInclude(c => c!.Household)
                    .Include(v => v.VehicleType)
                    .Where(v => v.IsActive)
                    .ToListAsync(cancellationToken);

                return vehicles.FirstOrDefault(v => NormalizePlate(v.LicensePlate) == norm);
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); /* Dùng fallback */ }
        }

        return null;
    }

    public Task<BlacklistedVehicle?> CheckBlacklistAsync(string licensePlate, CancellationToken cancellationToken = default)
        => CheckBlacklistAsync(licensePlate, cancellationToken, null);

    private async Task<BlacklistedVehicle?> CheckBlacklistAsync(string licensePlate, CancellationToken cancellationToken, DbProbe? probe)
    {
        var norm = NormalizePlate(licensePlate);
        if (string.IsNullOrEmpty(norm)) return null;

        if (_dbContextFactory != null && probe?.Failed != true)
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var blacklists = await db.BlacklistedVehicles
                    .Include(b => b.Customer)
                    .Where(b => b.IsActive)
                    .ToListAsync(cancellationToken);

                return blacklists.FirstOrDefault(b => NormalizePlate(b.LicensePlate) == norm);
            }
            catch (OperationCanceledException) { throw; }
            catch { probe?.MarkFailed(); /* Dùng fallback */ }
        }

        return null;
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
            return new GateCheckInResult { Success = false, IsPermissionDenied = true, RejectReason = CheckInRejectReason.PermissionDenied, Message = ex.Message };
        }

        var createdByUserId = request.CreatedByUserId ?? _guard.CurrentUserId;

        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return new GateCheckInResult { Success = false, RejectReason = CheckInRejectReason.EmptyPlate, Message = "Biển số xe không được để trống." };
        }

        if (!LicensePlateNormalizer.IsValid(request.LicensePlate))
        {
            return new GateCheckInResult
            {
                Success = false,
                RejectReason = CheckInRejectReason.PlateInvalid,
                Message = "Biển số không hợp lệ (cần 5 đến 12 ký tự chữ và số)."
            };
        }

        var cleanPlate = request.LicensePlate.Trim().ToUpperInvariant();
        var normPlate = NormalizePlate(cleanPlate);

        var probe = new DbProbe();
        VehicleClassification? classified = null;

        if (_dbContextFactory != null)
        {
            try
            {
                await using var classifyDb = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var nowUtc = DateTime.UtcNow;

                // 1. Danh sách cấm (bảng của nhóm quản lý khách hàng). Lỗi không phải mất kết nối thì từ chối (fail-closed).
                var blacklist = await GateClassificationQueries.FindActiveBlacklistAsync(classifyDb, normPlate, cancellationToken);
                if (blacklist != null)
                {
                    PlateOwnership? blockedOwner = null;
                    try
                    {
                        blockedOwner = await _plateOwnershipReader.ReadAsync(classifyDb, normPlate, nowUtc, cancellationToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* vẫn chặn; chỉ thiếu thông tin vé cho nhật ký */ }

                    var blocked = VehicleClassifier.Classify(normPlate, blacklist, blockedOwner, request.VehicleTypeId, nowUtc);
                    await _auditService.LogAsync(
                        GateAuditEntries.BlacklistBlocked(cleanPlate, normPlate, blocked.Ticket != null),
                        cancellationToken);
                    return BlacklistedResult(cleanPlate, blacklist);
                }

                // 2. Xe đang trong bãi (phiên Active trùng biển số đã chuẩn hoá)
                if (await GateClassificationQueries.HasActiveSessionAsync(classifyDb, normPlate, cancellationToken))
                {
                    return RejectedResult(CheckInRejectReason.AlreadyInside, VehicleCategory.Visitor, cleanPlate);
                }

                // 3. Chủ xe, hộ và vé tháng
                classified = await GateClassificationQueries.ClassifyAsync(
                    classifyDb, _plateOwnershipReader, null, normPlate, request.VehicleTypeId, nowUtc, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (DbConnectionHelper.IsConnectionException(ex))
            {
                probe.MarkFailed();
                classified = null;
            }
            catch (Exception ex) when (ex is not PermissionDeniedException and not AuditLockHeldException)
            {
                // Lỗi không phải mất kết nối: không đoán, không cho vào (tránh bỏ qua danh sách đen)
                System.Diagnostics.Debug.WriteLine($"[GateControlService Classification Error]: {ex.GetType().Name}");
                return new GateCheckInResult
                {
                    Success = false,
                    RejectReason = CheckInRejectReason.ClassificationFailed,
                    Message = "Không thể phân loại xe do lỗi hệ thống. Vui lòng thử lại."
                };
            }
        }

        if (classified == null)
        {
            // Ngoại tuyến: chỉ kiểm tra phiên trong bộ nhớ, coi là khách vãng lai
            probe.MarkFailed();
            var memoryActive = await GetActiveSessionsAsync(cancellationToken, probe);
            if (memoryActive.Any(s => NormalizePlate(s.LicensePlate) == normPlate))
            {
                return RejectedResult(CheckInRejectReason.AlreadyInside, VehicleCategory.Visitor, cleanPlate);
            }

            classified = ClassifyFromMemory(normPlate);
        }

        var classification = classified;
        var dbReachable = _dbContextFactory != null && !probe.Failed;

        var category = classification.Category;
        var ticket = classification.Ticket;
        var owner = classification.Owner;
        var isMonthly = classification.IsMonthlyPass;
        var vehicleTypeId = request.VehicleTypeId > 0 ? request.VehicleTypeId : (ticket?.VehicleTypeId ?? owner?.VehicleTypeId ?? 1);
        var activeOwner = owner is { OwnerIsActive: true, OwnerCustomerId: not null } ? owner : null;

        var ticketCode = isMonthly
            ? ticket!.TicketCode
            : $"TK-{DateTime.UtcNow:yyyyMMdd}-{Interlocked.Increment(ref _sessionSequence)}";

        // Lưu trữ an toàn vĩnh viễn tệp ảnh chụp khi xe vào (trước giao dịch có khoá nhật ký)
        var archivedImagePath = await _imageStorageService.ArchiveCaptureAsync(request.ImagePath, "CheckIn", cleanPlate);

        var vTypes = await GetVehicleTypesAsync(cancellationToken, probe);
        var matchedVType = vTypes.FirstOrDefault(v => v.VehicleTypeId == vehicleTypeId);

        dbReachable = dbReachable && !probe.Failed;

        var session = new ParkingSession
        {
            TicketCode = ticketCode,
            LicensePlate = cleanPlate,
            VehicleTypeId = vehicleTypeId,
            VehicleType = matchedVType,
            CheckInTime = DateTime.UtcNow,
            CheckInImagePath = archivedImagePath ?? request.ImagePath,
            Status = SessionStatus.Active,
            IsMonthlyPass = isMonthly,
            CustomerId = activeOwner?.OwnerCustomerId,
            Customer = activeOwner == null ? null : new Customer
            {
                CustomerId = activeOwner.OwnerCustomerId!.Value,
                FullName = activeOwner.OwnerName ?? string.Empty,
                HouseholdId = activeOwner.HouseholdId
            },
            CustomerType = category == VehicleCategory.Resident ? CustomerType.Resident : CustomerType.External,
            CreatedByUserId = createdByUserId,
            TotalFee = 0
        };

        ParkingSlot? assignedSlot = null;
        SlotCandidate? allocatedSlot = null;
        var dbAuditWritten = false;
        BlacklistMatch? blockedBy = null;
        var rejection = CheckInRejectReason.None;
        int? requestedSlotId = request.SlotId is > 0 ? request.SlotId : null;

        if (dbReachable)
        {
            try
            {
                await using var db = await _dbContextFactory!.CreateDbContextAsync(cancellationToken);
                await using (var transaction = await _auditService.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
                {
                    // Kiểm tra lại danh sách đen trong giao dịch: biển số có thể vừa được thêm sau khi phân loại.
                    blockedBy = await GateClassificationQueries.FindActiveBlacklistAsync(db, normPlate, cancellationToken);

                    // Kiểm tra lại xe chưa ra bãi trong giao dịch: hai lượt vào song song cùng biển số chỉ một lượt thành công
                    var alreadyInside = blockedBy == null
                        && await GateClassificationQueries.HasActiveSessionAsync(db, normPlate, cancellationToken);
                    if (alreadyInside)
                    {
                        rejection = CheckInRejectReason.AlreadyInside;
                    }
                    else if (blockedBy == null)
                    {
                        var (slot, reason) = await GateSlotAllocator.AllocateAsync(
                            db, vehicleTypeId, category, requestedSlotId, cleanPlate, cancellationToken);
                        if (reason != CheckInRejectReason.None)
                        {
                            rejection = reason;
                        }
                        else
                        {
                            allocatedSlot = slot;
                            var dbSession = new ParkingSession
                            {
                                TicketCode = session.TicketCode,
                                LicensePlate = session.LicensePlate,
                                VehicleTypeId = session.VehicleTypeId,
                                SlotId = slot?.SlotId,
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
                            await db.SaveChangesAsync(cancellationToken);

                            // Cột CustomerType có giá trị mặc định External trong cấu hình bảng nên EF bỏ qua Resident (= 0) khi chèn.
                            // Luôn ghi lại rõ ràng, không phụ thuộc giá trị EF đọc lại; có thể bỏ khi mặc định được sửa.
                            if (session.CustomerType == CustomerType.Resident)
                            {
                                dbSession.CustomerType = CustomerType.Resident;
                                db.Entry(dbSession).Property(x => x.CustomerType).IsModified = true;
                            }

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
                                    SlotCode = slot?.SlotCode,
                                    IsMonthlyPass = isMonthly,
                                    Category = category.ToString(),
                                    CustomerId = session.CustomerId,
                                    ZoneCode = slot?.ZoneCode
                                }), cancellationToken);
                            await db.SaveChangesAsync(cancellationToken);
                            await transaction.CommitAsync(cancellationToken);

                            session.SessionId = dbSession.SessionId;
                            dbAuditWritten = true;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (GateSlotAllocator.IsActiveSlotUniqueViolation(ex))
            {
                // Chốt chặn cuối: ô vừa bị phiên khác chiếm. Đây là từ chối nghiệp vụ, không phải mất kết nối.
                rejection = CheckInRejectReason.NoSlotAvailable;
            }
            catch (Exception ex) when (DbConnectionHelper.IsConnectionException(ex))
            {
                // Mất kết nối cơ sở dữ liệu: tiếp tục ở chế độ ngoại tuyến (bộ nhớ + JSONL)
                System.Diagnostics.Debug.WriteLine($"[GateControlService DB Error]: {ex.Message}");
                allocatedSlot = null;
                blockedBy = null;
                rejection = CheckInRejectReason.None;
            }
            catch (Exception ex)
            {
                // Lỗi khác trong giao dịch (tra cứu lại, khoá ngoại, ghi nhật ký, hết thời gian chờ khóa...): không lưu ngoại tuyến
                System.Diagnostics.Debug.WriteLine($"[GateControlService Check-in Persist Error]: {ex.GetType().Name}");
                return new GateCheckInResult
                {
                    Success = false,
                    RejectReason = CheckInRejectReason.PersistFailed,
                    Message = "Không thể ghi nhận lượt vào bãi. Vui lòng thử lại."
                };
            }

            if (blockedBy != null)
            {
                // Ghi nhật ký sau khi giao dịch đã đóng (không giữ khoá nhật ký)
                await _auditService.LogAsync(
                    GateAuditEntries.BlacklistBlocked(cleanPlate, normPlate, ticket != null),
                    cancellationToken);
                return BlacklistedResult(cleanPlate, blockedBy);
            }

            if (rejection != CheckInRejectReason.None)
            {
                return RejectedResult(rejection, category, cleanPlate);
            }

            if (allocatedSlot != null)
            {
                assignedSlot = new ParkingSlot
                {
                    SlotId = allocatedSlot.SlotId,
                    SlotCode = allocatedSlot.SlotCode,
                    VehicleTypeId = allocatedSlot.VehicleTypeId,
                    ZoneId = allocatedSlot.ZoneId,
                    Status = SlotStatus.Occupied,
                    CurrentLicensePlate = cleanPlate
                };
                session.SlotId = assignedSlot.SlotId;
                session.Slot = assignedSlot;
            }
        }

        // Lưu bộ nhớ đệm và tệp ngoại tuyến (đồng bộ luồng)
        lock (_syncLock)
        {
            if (!dbAuditWritten && assignedSlot == null)
            {
                // Chế độ ngoại tuyến: dùng ô được chỉ định nếu còn trống và đúng loại xe, ngược lại ô trống đầu tiên trong bộ nhớ
                assignedSlot = requestedSlotId.HasValue
                    ? _memorySlots.FirstOrDefault(s => s.SlotId == requestedSlotId.Value && s.VehicleTypeId == vehicleTypeId && s.Status == SlotStatus.Available)
                    : null;
                assignedSlot ??= _memorySlots.FirstOrDefault(s => s.VehicleTypeId == vehicleTypeId && s.Status == SlotStatus.Available);
                session.SlotId = assignedSlot?.SlotId;
                session.Slot = assignedSlot;
            }

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
            Message = category switch
            {
                VehicleCategory.Resident => "Cư dân vào bãi thành công.",
                VehicleCategory.MonthlyPass => "Xe vé tháng vào bãi thành công.",
                _ => "Xe vãng lai vào bãi thành công."
            },
            Session = session,
            Category = category,
            IsMonthlyTicket = isMonthly,
            IsResident = category == VehicleCategory.Resident,
            ApartmentCode = category == VehicleCategory.Resident ? owner?.ApartmentCode : null,
            TicketValidUntilUtc = ticket?.EndDateUtc,
            CustomerLockedWarning = classification.Warning == ClassificationWarning.CustomerLocked,
            TicketVehicleTypeMismatchWarning = classification.Warning == ClassificationWarning.TicketVehicleTypeMismatch,
            CustomerName = activeOwner?.OwnerName,
            AssignedSlotCode = assignedSlot?.SlotCode,
            AssignedZoneCode = allocatedSlot?.ZoneCode,
            AssignedZoneAudience = allocatedSlot?.Audience
        };
    }

    /// <summary>Ngoại tuyến không có dữ liệu chủ xe và vé: mọi xe được coi là khách vãng lai.</summary>
    private static VehicleClassification ClassifyFromMemory(string norm) => VehicleClassification.Visitor(norm);

    private static GateCheckInResult BlacklistedResult(string licensePlate, BlacklistMatch match) => new()
    {
        Success = false,
        Category = VehicleCategory.Blacklisted,
        RejectReason = CheckInRejectReason.Blacklisted,
        IsBlacklisted = true,
        BlacklistReason = match.Reason,
        Message = $"XE BỊ CẤM VÀO BÃI! Biển số '{licensePlate}' nằm trong danh sách đen: {match.Reason}"
    };

    private static GateCheckInResult RejectedResult(CheckInRejectReason reason, VehicleCategory category, string licensePlate) => new()
    {
        Success = false,
        Category = category,
        RejectReason = reason,
        Message = reason switch
        {
            CheckInRejectReason.NoSlotAvailable => $"Hết chỗ cho {CategoryGroupName(category)}",
            CheckInRejectReason.AlreadyInside => $"Biển số '{licensePlate}' hiện đang có phiên gửi xe chưa xuất bãi!",
            CheckInRejectReason.SlotNotFound => "Không tìm thấy ô đỗ được chỉ định.",
            CheckInRejectReason.SlotVehicleTypeMismatch => "Ô đỗ được chỉ định không dành cho loại xe này.",
            CheckInRejectReason.SlotAudienceNotAllowed => $"Ô đỗ được chỉ định không dành cho {CategoryGroupName(category)}.",
            CheckInRejectReason.SlotNotAvailable => "Ô đỗ được chỉ định không còn trống.",
            _ => "Không thể cấp ô đỗ cho xe."
        }
    };

    private static string CategoryGroupName(VehicleCategory category) => category switch
    {
        VehicleCategory.Resident => "cư dân",
        VehicleCategory.MonthlyPass => "khách vé tháng",
        _ => "khách vãng lai"
    };

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

                // Phí luôn do máy chủ tính lại bằng công thức và bảng giá hiện hành; số tiền từ máy trạm bị bỏ qua.
                var checkOutTime = DateTime.UtcNow;
                var pricingRule = await db.PricingRules.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.VehicleTypeId == session.VehicleTypeId, cancellationToken);
                if (pricingRule == null)
                {
                    lock (_syncLock)
                    {
                        pricingRule = _memoryPricingRules.FirstOrDefault(r => r.VehicleTypeId == session.VehicleTypeId);
                    }
                }

                var serverFee = _feeCalculator.CalculateFee(session, pricingRule, checkOutTime).TotalFee;
                var method = serverFee <= 0 ? PaymentMethod.Free : request.PaymentMethod;
                if (serverFee < 0 || (serverFee > 0 && method == PaymentMethod.Free))
                    return new GateCheckOutResult { Success = false, Message = "Số tiền hoặc phương thức thanh toán không hợp lệ." };
                if (serverFee > 0 && method == PaymentMethod.VietQR)
                    return new GateCheckOutResult { Success = false, Message = "Thanh toán VietQR phải được xác nhận qua cổng thanh toán." };

                await ShiftAccounting.AddParkingFeeAsync(db, shift, request.ActorUserId,
                    session.SessionId, method, serverFee, null, cancellationToken);
                session.CheckOutTime = checkOutTime;
                session.CheckOutImagePath = archivedOutImage ?? request.CheckOutImagePath;
                session.TotalFee = serverFee;
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
                        Fee = serverFee,
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
                AppendAuditLog("CHECK_OUT", completedSession, completedSession.TotalFee, completedMethod.ToString());
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

