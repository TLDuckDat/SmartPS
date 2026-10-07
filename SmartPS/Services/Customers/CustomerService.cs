using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.GateControl;

namespace SmartPS.Services.Customers;

public sealed class CustomerService : ICustomerService
{
    private const int MaxPageSize = 200;

    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public CustomerService(
        IDbContextFactory<SmartPsDbContext> contextFactory,
        IAuthorizationGuard guard,
        IAuditService audit,
        TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = timeProvider ?? TimeProvider.System;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    // ---- reads ---------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.VehicleTypes.AsNoTracking().OrderBy(v => v.VehicleTypeId).ToListAsync(cancellationToken);
    }

    public async Task<CustomerPage> SearchAsync(CustomerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _guard.DemandAsync(Permissions.CustomerView, "Customer", null, cancellationToken);

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var pageIndex = Math.Max(0, query.PageIndex);
        var now = UtcNow;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Customer> customers = db.Customers.AsNoTracking();

        customers = query.Filter switch
        {
            CustomerListFilter.Residents => customers.Where(c => c.IsResident),
            CustomerListFilter.NonResidents => customers.Where(c => !c.IsResident),
            _ => customers
        };

        var text = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var textPattern = "%" + EscapeLike(text) + "%";
            var phoneFragment = CustomerValidator.NormalizePhone(text);
            var phonePattern = "%" + EscapeLike(phoneFragment.Length > 0 ? phoneFragment : text) + "%";
            var plateFragment = LicensePlateNormalizer.Normalize(text);
            customers = customers.Where(c =>
                EF.Functions.ILike(c.FullName, textPattern, "\\")
                || EF.Functions.ILike(c.PhoneNumber, phonePattern, "\\")
                || (c.ApartmentCode != null && EF.Functions.ILike(c.ApartmentCode, textPattern, "\\"))
                || (plateFragment != "" && c.Vehicles.Any(v => v.IsActive && v.LicensePlate.Contains(plateFragment))));
        }

        var total = await customers.CountAsync(cancellationToken);
        var page = await customers
            .OrderBy(c => c.FullName)
            .ThenBy(c => c.CustomerId)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = page.Select(c => c.CustomerId).ToList();
        var plates = await db.CustomerVehicles.AsNoTracking()
            .Where(v => v.IsActive && ids.Contains(v.CustomerId))
            .OrderBy(v => v.CustomerVehicleId)
            .Select(v => new { v.CustomerId, v.LicensePlate })
            .ToListAsync(cancellationToken);
        var tickets = await db.MonthlyTickets.AsNoTracking()
            .Where(t => ids.Contains(t.CustomerId))
            .ToListAsync(cancellationToken);

        var items = page.Select(c =>
        {
            var primary = TicketStatusEvaluator.PickPrimary(
                tickets.Where(t => t.CustomerId == c.CustomerId).Select(t => TicketMapping.ToDto(t, null, now)));
            return new CustomerListItem(
                c.CustomerId,
                c.FullName,
                c.PhoneNumber,
                c.IsResident,
                c.ApartmentCode,
                c.Building,
                c.Type,
                c.IsActive,
                plates.Where(p => p.CustomerId == c.CustomerId).Select(p => p.LicensePlate).ToList(),
                primary?.TicketCode,
                primary?.EndDateUtc,
                primary?.DisplayStatus ?? TicketDisplayStatus.None);
        }).ToList();

        return new CustomerPage(items, total, pageIndex, pageSize);
    }

    public async Task<CustomerSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await _guard.DemandAsync(Permissions.CustomerView, "Customer", null, cancellationToken);

        var now = UtcNow;
        var soon = now + TicketStatusEvaluator.ExpiringSoonWindow;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var totalCustomers = await db.Customers.AsNoTracking().CountAsync(cancellationToken);
        var residents = await db.Customers.AsNoTracking().CountAsync(c => c.IsResident, cancellationToken);
        var current = db.MonthlyTickets.AsNoTracking()
            .Where(t => t.Status == MonthlyTicketStatus.Active && t.StartDate <= now && t.EndDate > now);
        var activeTickets = await current.CountAsync(cancellationToken);
        var expiringSoon = await current.CountAsync(t => t.EndDate <= soon, cancellationToken);
        var revenue = await current.SumAsync(t => t.MonthlyPrice, cancellationToken);

        return new CustomerSummary(totalCustomers, residents, activeTickets, expiringSoon, revenue);
    }

    public async Task<CustomerDetails?> GetDetailsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await _guard.DemandAsync(Permissions.CustomerView, "Customer", customerId.ToString(), cancellationToken);

        var now = UtcNow;
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var vehicles = await db.CustomerVehicles.AsNoTracking()
            .Include(v => v.VehicleType)
            .Where(v => v.CustomerId == customerId)
            .OrderBy(v => v.CustomerVehicleId)
            .ToListAsync(cancellationToken);
        var tickets = await db.MonthlyTickets.AsNoTracking()
            .Include(t => t.Plan)
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.EndDate)
            .ThenByDescending(t => t.TicketId)
            .ToListAsync(cancellationToken);

        return new CustomerDetails(
            customer.CustomerId,
            customer.FullName,
            customer.PhoneNumber,
            customer.Email,
            customer.IdentityCard,
            customer.IsResident,
            customer.ApartmentCode,
            customer.Building,
            customer.Type,
            customer.IsActive,
            customer.Notes,
            customer.CreatedAt,
            vehicles.Select(v => new CustomerVehicleDto(
                v.CustomerVehicleId, v.CustomerId, v.LicensePlate, v.VehicleTypeId, v.VehicleType?.TypeName ?? string.Empty,
                v.IsActive, v.CreatedAt, v.RemovedAt)).ToList(),
            tickets.Select(t => TicketMapping.ToDto(t, t.Plan?.PlanName, now)).ToList());
    }

    // ---- writes --------------------------------------------------------------------------------------------------

    public async Task<OperationResult<int>> CreateCustomerAsync(CustomerUpsertRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "Customer", null, cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult<int>.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var errors = CustomerValidator.Validate(request);
        if (errors.Count > 0)
        {
            return OperationResult<int>.Fail(OperationError.Validation, "Dữ liệu khách hàng không hợp lệ.", errors);
        }

        var vehicles = request.Vehicles
            .Select(v => new NewVehicle(LicensePlateNormalizer.Normalize(v.LicensePlate), v.VehicleTypeId))
            .ToList();
        var phone = CustomerValidator.NormalizePhone(request.PhoneNumber);
        var type = CustomerValidator.NormalizeType(request.IsResident, request.Type);
        var apartment = CustomerValidator.NormalizeApartmentCode(request.ApartmentCode);
        var building = NullIfBlank(request.Building);
        var email = NullIfBlank(request.Email);
        var identityCard = NullIfBlank(request.IdentityCard);

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var plates = vehicles.Select(v => v.LicensePlate).ToList();
                if (plates.Count > 0)
                {
                    if (await db.CustomerVehicles.AnyAsync(v => v.IsActive && plates.Contains(v.LicensePlate), cancellationToken))
                    {
                        return OperationResult<int>.Fail(OperationError.PlateOwnedByOtherCustomer, "Biển số đã thuộc khách hàng khác.");
                    }

                    var typeIds = vehicles.Select(v => v.VehicleTypeId).Distinct().ToList();
                    if (await db.VehicleTypes.CountAsync(t => typeIds.Contains(t.VehicleTypeId), cancellationToken) != typeIds.Count)
                    {
                        return OperationResult<int>.Fail(OperationError.Validation, "Loại phương tiện không tồn tại.",
                            new[] { CustomerValidationError.VehicleTypeRequired });
                    }
                }

                var now = UtcNow;
                var customer = new Customer
                {
                    FullName = request.FullName.Trim(),
                    PhoneNumber = phone,
                    Email = email,
                    IdentityCard = identityCard,
                    IsResident = request.IsResident,
                    ApartmentCode = apartment,
                    Building = building,
                    Type = type,
                    Notes = NullIfBlank(request.Notes),
                    IsActive = true,
                    CreatedAt = now,
                    DefaultLicensePlate = vehicles.Count > 0 ? vehicles[0].LicensePlate : string.Empty,
                    VehicleTypeId = vehicles.Count > 0 ? vehicles[0].VehicleTypeId : null
                };
                db.Customers.Add(customer);
                await db.SaveChangesAsync(cancellationToken);

                var created = vehicles.Select(v => new CustomerVehicle
                {
                    CustomerId = customer.CustomerId,
                    LicensePlate = v.LicensePlate,
                    VehicleTypeId = v.VehicleTypeId,
                    IsActive = true,
                    CreatedAt = now
                }).ToList();
                db.CustomerVehicles.AddRange(created);
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.CustomerCreate,
                    AuditOutcome.Success,
                    "Customer",
                    customer.CustomerId.ToString(),
                    new
                    {
                        FullName = customer.FullName,
                        PhoneNumber = AuditPii.MaskPhone(phone),
                        IsResident = customer.IsResident,
                        ApartmentCode = customer.ApartmentCode,
                        Building = customer.Building,
                        Type = customer.Type.ToString(),
                        HasEmail = email is not null,
                        HasIdentityCard = identityCard is not null
                    }), cancellationToken);
                foreach (var vehicle in created)
                {
                    await _audit.AppendAsync(db, VehicleAddEntry(vehicle), cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult<int>.Ok(customer.CustomerId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return OperationResult<int>.Fail(OperationError.PlateOwnedByOtherCustomer, "Biển số đã thuộc khách hàng khác.");
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult> UpdateCustomerAsync(int customerId, CustomerUpsertRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "Customer", customerId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
                if (customer is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy khách hàng.");
                }

                var errors = CustomerValidator.Validate(request with { Vehicles = Array.Empty<NewVehicle>() });
                if (errors.Count > 0)
                {
                    return OperationResult.Fail(OperationError.Validation, "Dữ liệu khách hàng không hợp lệ.", errors);
                }

                var before = Snapshot(customer);
                var email = NullIfBlank(request.Email);
                var identityCard = NullIfBlank(request.IdentityCard);
                var notes = NullIfBlank(request.Notes);
                var emailChanged = !string.Equals(customer.Email ?? string.Empty, email ?? string.Empty, StringComparison.Ordinal);
                var identityCardChanged = !string.Equals(customer.IdentityCard ?? string.Empty, identityCard ?? string.Empty, StringComparison.Ordinal);
                var notesChanged = !string.Equals(customer.Notes ?? string.Empty, notes ?? string.Empty, StringComparison.Ordinal);

                customer.FullName = request.FullName.Trim();
                customer.PhoneNumber = CustomerValidator.NormalizePhone(request.PhoneNumber);
                customer.Email = email;
                customer.IdentityCard = identityCard;
                customer.IsResident = request.IsResident;
                customer.ApartmentCode = CustomerValidator.NormalizeApartmentCode(request.ApartmentCode);
                customer.Building = NullIfBlank(request.Building);
                customer.Type = CustomerValidator.NormalizeType(request.IsResident, request.Type);
                customer.Notes = notes;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.CustomerUpdate,
                    AuditOutcome.Success,
                    "Customer",
                    customer.CustomerId.ToString(),
                    new
                    {
                        Before = before,
                        After = Snapshot(customer),
                        EmailChanged = emailChanged,
                        IdentityCardChanged = identityCardChanged,
                        NotesChanged = notesChanged
                    }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult> SetCustomerActiveAsync(int customerId, bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "Customer", customerId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
                if (customer is null)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy khách hàng.");
                }

                if (customer.IsActive == isActive)
                {
                    return OperationResult.Ok();
                }

                var wasActive = customer.IsActive;
                customer.IsActive = isActive;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.CustomerUpdate,
                    AuditOutcome.Success,
                    "Customer",
                    customer.CustomerId.ToString(),
                    new
                    {
                        Before = new { IsActive = wasActive },
                        After = new { IsActive = isActive },
                        EmailChanged = false,
                        IdentityCardChanged = false,
                        NotesChanged = false
                    }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult<int>> AddVehicleAsync(int customerId, NewVehicle vehicle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "Customer", customerId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult<int>.Fail(OperationError.PermissionDenied, ex.Message);
        }

        var plate = LicensePlateNormalizer.Normalize(vehicle.LicensePlate);

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
                if (customer is null)
                {
                    return OperationResult<int>.Fail(OperationError.NotFound, "Không tìm thấy khách hàng.");
                }

                if (!LicensePlateNormalizer.IsValid(plate))
                {
                    return OperationResult<int>.Fail(OperationError.PlateInvalid, "Biển số không hợp lệ.",
                        new[] { CustomerValidationError.PlateInvalid });
                }

                if (vehicle.VehicleTypeId <= 0 || !await db.VehicleTypes.AnyAsync(t => t.VehicleTypeId == vehicle.VehicleTypeId, cancellationToken))
                {
                    return OperationResult<int>.Fail(OperationError.Validation, "Loại phương tiện không hợp lệ.",
                        new[] { CustomerValidationError.VehicleTypeRequired });
                }

                var owner = await db.CustomerVehicles
                    .Where(v => v.IsActive && v.LicensePlate == plate)
                    .Select(v => (int?)v.CustomerId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (owner == customerId)
                {
                    return OperationResult<int>.Fail(OperationError.PlateAlreadyOnCustomer, "Biển số đã có trong danh sách xe của khách.");
                }

                if (owner is not null)
                {
                    return OperationResult<int>.Fail(OperationError.PlateOwnedByOtherCustomer, "Biển số đã thuộc khách hàng khác.");
                }

                var created = new CustomerVehicle
                {
                    CustomerId = customerId,
                    LicensePlate = plate,
                    VehicleTypeId = vehicle.VehicleTypeId,
                    IsActive = true,
                    CreatedAt = UtcNow
                };
                db.CustomerVehicles.Add(created);
                await db.SaveChangesAsync(cancellationToken);

                await RefreshMirrorAsync(db, customer, cancellationToken);
                await _audit.AppendAsync(db, VehicleAddEntry(created), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult<int>.Ok(created.CustomerVehicleId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return OperationResult<int>.Fail(OperationError.PlateOwnedByOtherCustomer, "Biển số đã thuộc khách hàng khác.");
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    public async Task<OperationResult> RemoveVehicleAsync(int customerVehicleId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _guard.DemandAsync(Permissions.CustomerManage, "CustomerVehicle", customerVehicleId.ToString(), cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var vehicle = await db.CustomerVehicles.FirstOrDefaultAsync(v => v.CustomerVehicleId == customerVehicleId, cancellationToken);
                if (vehicle is null || !vehicle.IsActive)
                {
                    return OperationResult.Fail(OperationError.NotFound, "Không tìm thấy phương tiện.");
                }

                var now = UtcNow;
                var plate = vehicle.LicensePlate;
                var customerId = vehicle.CustomerId;
                if (await db.MonthlyTickets.AnyAsync(t => t.CustomerId == customerId
                                                          && t.RegisteredLicensePlate == plate
                                                          && t.Status == MonthlyTicketStatus.Active
                                                          && t.EndDate > now, cancellationToken))
                {
                    return OperationResult.Fail(OperationError.VehicleHasActiveTicket,
                        "Xe đang có vé tháng còn hiệu lực. Hãy tạm ngưng vé trước khi gỡ xe.");
                }

                vehicle.IsActive = false;
                vehicle.RemovedAt = now;
                await db.SaveChangesAsync(cancellationToken);

                var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
                if (customer is not null)
                {
                    await RefreshMirrorAsync(db, customer, cancellationToken);
                }

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.CustomerVehicleRemove,
                    AuditOutcome.Success,
                    "CustomerVehicle",
                    vehicle.CustomerVehicleId.ToString(),
                    new { CustomerId = customerId, LicensePlate = plate }), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return OperationResult.Ok();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(OperationError.DatabaseError, ex.Message);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>DefaultLicensePlate / VehicleTypeId chỉ là bản sao để hoàn tác migration: luôn là xe đang hoạt động sớm nhất.</summary>
    private static async Task RefreshMirrorAsync(SmartPsDbContext db, Customer customer, CancellationToken cancellationToken)
    {
        var earliest = await db.CustomerVehicles
            .Where(v => v.CustomerId == customer.CustomerId && v.IsActive)
            .OrderBy(v => v.CreatedAt)
            .ThenBy(v => v.CustomerVehicleId)
            .FirstOrDefaultAsync(cancellationToken);
        customer.DefaultLicensePlate = earliest?.LicensePlate ?? string.Empty;
        customer.VehicleTypeId = earliest?.VehicleTypeId;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AuditEntry VehicleAddEntry(CustomerVehicle vehicle)
        => new(
            AuditActions.CustomerVehicleAdd,
            AuditOutcome.Success,
            "CustomerVehicle",
            vehicle.CustomerVehicleId.ToString(),
            new { CustomerId = vehicle.CustomerId, LicensePlate = vehicle.LicensePlate, VehicleTypeId = vehicle.VehicleTypeId });

    private static object Snapshot(Customer customer)
        => new
        {
            FullName = customer.FullName,
            PhoneNumber = AuditPii.MaskPhone(customer.PhoneNumber),
            IsResident = customer.IsResident,
            ApartmentCode = customer.ApartmentCode,
            Building = customer.Building,
            Type = customer.Type.ToString(),
            IsActive = customer.IsActive
        };

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
