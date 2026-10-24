using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Settings;

public sealed class SystemSettingsService : ISystemSettingsService
{
    private const string EntityType = "ParkingSettings";

    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;

    public SystemSettingsService(IDbContextFactory<SmartPsDbContext> contextFactory, IAuthorizationGuard guard, IAuditService audit)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<OperationalSettings> GetOperationalSettingsAsync(CancellationToken cancellationToken = default)
    {
        var defaults = new ParkingSettings();
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var settings = await db.ParkingSettings.AsNoTracking().OrderBy(s => s.SettingsId).FirstOrDefaultAsync(cancellationToken) ?? defaults;
            return new OperationalSettings(settings.OverstayThresholdHours, settings.DefaultMaxVehiclesPerHousehold);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new OperationalSettings(defaults.OverstayThresholdHours, defaults.DefaultMaxVehiclesPerHousehold);
        }
    }

    public async Task<OperationResult> UpdateOperationalSettingsAsync(OperationalSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            await _guard.DemandAsync(Permissions.SettingsManage, EntityType, null, cancellationToken);
        }
        catch (PermissionDeniedException ex)
        {
            return OperationResult.Fail(OperationError.PermissionDenied, ex.Message);
        }

        if (settings.OverstayThresholdHours is < OperationalSettings.MinOverstayHours or > OperationalSettings.MaxOverstayHours)
            return OperationResult.Fail(OperationError.Validation,
                AppText.Get("Msg_SetSvc_OverstayRange", OperationalSettings.MinOverstayHours, OperationalSettings.MaxOverstayHours));
        if (settings.MaxVehiclesPerHousehold is < OperationalSettings.MinVehiclesPerHousehold or > OperationalSettings.MaxVehiclesPerHouseholdLimit)
            return OperationResult.Fail(OperationError.Validation,
                AppText.Get("Msg_SetSvc_VehiclesRange", OperationalSettings.MinVehiclesPerHousehold, OperationalSettings.MaxVehiclesPerHouseholdLimit));

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using (var transaction = await _audit.BeginAuditedTransactionAsync(db, System.Data.IsolationLevel.ReadCommitted, cancellationToken))
            {
                var row = await db.ParkingSettings.OrderBy(s => s.SettingsId).FirstOrDefaultAsync(cancellationToken);
                if (row is null)
                {
                    row = new ParkingSettings();
                    db.ParkingSettings.Add(row);
                }

                var before = new OperationalSettings(row.OverstayThresholdHours, row.DefaultMaxVehiclesPerHousehold);
                if (before == settings && row.SettingsId != 0)
                {
                    return OperationResult.Ok();
                }

                row.OverstayThresholdHours = settings.OverstayThresholdHours;
                row.DefaultMaxVehiclesPerHousehold = settings.MaxVehiclesPerHousehold;
                await db.SaveChangesAsync(cancellationToken);

                await _audit.AppendAsync(db, new AuditEntry(
                    AuditActions.SettingsUpdate,
                    AuditOutcome.Success,
                    EntityType,
                    row.SettingsId.ToString(),
                    new
                    {
                        Before = new { before.OverstayThresholdHours, before.MaxVehiclesPerHousehold },
                        After = new { settings.OverstayThresholdHours, settings.MaxVehiclesPerHousehold }
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

    public async Task<DatabaseStatus> GetDatabaseStatusAsync(CancellationToken cancellationToken = default)
    {
        var server = string.Empty;
        var database = string.Empty;

        // Mọi lỗi (kể cả chuỗi kết nối sai) đều trả trạng thái mất kết nối để màn Cài đặt không kẹt ở "Đang kiểm tra..."
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var builder = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
            server = builder.DataSource ?? string.Empty;
            database = builder.InitialCatalog ?? string.Empty;

            var connection = (SqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            var serverVersion = connection.ServerVersion;
            await connection.CloseAsync();

            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();

            return new DatabaseStatus(true, server, database, serverVersion, applied.Count, pending, applied.LastOrDefault(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DatabaseStatus(false, server, database, null, 0, 0, null, ex.Message);
        }
    }
}
