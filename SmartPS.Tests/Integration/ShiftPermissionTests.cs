using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Services.Shifts;

namespace SmartPS.Tests.Integration;

/// <summary>AC-16 (Shift.Review / Shift.Adjust instead of role names), R5 Shift.Open/Close, T-ACTOR, T-R15b, R14 shift events.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ShiftPermissionTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public ShiftPermissionTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static IShiftService Shifts(IServiceProvider sp) => sp.GetRequiredService<IShiftService>();

    private async Task<ShiftStatus> StatusAsync(int shiftId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.Shifts.Where(s => s.ShiftId == shiftId).Select(s => s.Status).SingleAsync();
    }

    /// <summary>Operator opens and closes a shift; returns the locked shift id.</summary>
    private async Task<int> LockedShiftAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var shift = await Shifts(sp).OpenShiftAsync(op.UserId, 100_000m);
        await Shifts(sp).CloseShiftAsync(shift.ShiftId, op.UserId, 100_000m);
        return shift.ShiftId;
    }

    [Fact]
    public async Task Operator_opens_and_closes_own_shift_with_audit_rows()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var shift = await Shifts(sp).OpenShiftAsync(op.UserId, 150_000m);
        var closed = await Shifts(sp).CloseShiftAsync(shift.ShiftId, op.UserId, 140_000m);

        Assert.Equal(ShiftStatus.Locked, closed.Status);
        var open = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ShiftOpen));
        Assert.Equal(AuditOutcome.Success, open.Outcome);
        Assert.Equal("Shift", open.EntityType);
        Assert.Equal(shift.ShiftId.ToString(), open.EntityId);
        Assert.Equal(150_000m, AuditDb.Details(open).GetProperty("beginningCash").GetDecimal());

        var close = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ShiftClose));
        Assert.Equal(AuditOutcome.Success, close.Outcome);
        Assert.Equal(shift.ShiftId.ToString(), close.EntityId);
        AuditDb.HasKeys(close, "expectedCash", "actualCash", "difference");
        Assert.Equal(140_000m, AuditDb.Details(close).GetProperty("actualCash").GetDecimal());
        Assert.Equal(-10_000m, AuditDb.Details(close).GetProperty("difference").GetDecimal());
    }

    [Fact]
    public async Task AC16_manager_review_succeeds_and_is_audited()
    {
        _db.RequireAvailable();
        var shiftId = await LockedShiftAsync();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var reviewed = await Shifts(sp).ReviewShiftAsync(shiftId, manager.UserId, "ok ca");

        Assert.Equal(ShiftStatus.Reviewed, reviewed.Status);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ShiftReview));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("Shift", row.EntityType);
        Assert.Equal(shiftId.ToString(), row.EntityId);
        Assert.Equal(manager.UserId, row.UserId);
        Assert.Equal("ok ca", AuditDb.String(AuditDb.Details(row), "note"));
    }

    [Fact]
    public async Task AC16_operator_review_is_denied_by_permission_and_audited()
    {
        _db.RequireAvailable();
        var shiftId = await LockedShiftAsync();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Shifts(sp).ReviewShiftAsync(shiftId, op.UserId, "x"));

        Assert.Equal(new[] { Permissions.ShiftReview }, ex.RequiredPermissions);
        Assert.Equal(ShiftStatus.Locked, await StatusAsync(shiftId));
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(new[] { Permissions.ShiftReview }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task AC16_review_follows_permission_not_role_name()
    {
        // A role named neither Manager nor Admin but holding Shift.Review can review.
        _db.RequireAvailable();
        var shiftId = await LockedShiftAsync();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("auditor"), Permissions.ShiftView, Permissions.ShiftReview);
        var reviewer = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(reviewer.Username);

        var reviewed = await Shifts(sp).ReviewShiftAsync(shiftId, reviewer.UserId, null);

        Assert.Equal(ShiftStatus.Reviewed, reviewed.Status);
    }

    [Fact]
    public async Task AC16_manager_adjust_succeeds_and_operator_adjust_is_denied()
    {
        _db.RequireAvailable();
        var shiftId = await LockedShiftAsync();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");

        using (var opSession = IntegrationServices.Create(_db))
        {
            await opSession.LoginAsync(op.Username);
            var idBeforeOp = await AuditDb.MaxIdAsync(_db.Factory);
            var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Shifts(opSession).CreateManualTransactionAsync(
                shiftId, op.UserId, FinancialTransactionType.Adjustment, PaymentMethod.Cash, 10_000m, null, "op try"));
            Assert.Equal(new[] { Permissions.ShiftAdjust }, ex.RequiredPermissions);
            var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBeforeOp));
            Assert.Equal(AuditActions.AccessDenied, denied.Action);
        }

        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var tx = await Shifts(sp).CreateManualTransactionAsync(
            shiftId, manager.UserId, FinancialTransactionType.Adjustment, PaymentMethod.Cash, 10_000m, "REF-1", "manager adjust");

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ShiftAdjustment));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("FinancialTransaction", row.EntityType);
        Assert.Equal(tx.TransactionId.ToString(), row.EntityId);
        AuditDb.HasKeys(row, "shiftId", "type", "paymentMethod", "amount", "transactionCode");
        Assert.Equal(10_000m, AuditDb.Details(row).GetProperty("amount").GetDecimal());
        Assert.Equal(tx.TransactionCode, AuditDb.String(AuditDb.Details(row), "transactionCode"));
        await using var ctx = _db.CreateContext();
        Assert.Equal(1, await ctx.FinancialTransactions.CountAsync(t => t.ShiftId == shiftId && t.Type == FinancialTransactionType.Adjustment));
    }

    [Fact]
    public async Task TACTOR_close_shift_with_mismatched_actor_is_denied()
    {
        // T-ACTOR: Operator logged in; CloseShiftAsync(actorUserId: other) → ActorMismatch + ACCESS_DENIED.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var other = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var shift = await Shifts(sp).OpenShiftAsync(op.UserId, 0m);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Shifts(sp).CloseShiftAsync(shift.ShiftId, other.UserId, 0m));

        Assert.Equal("ActorMismatch", ex.Reason);
        Assert.Equal(ShiftStatus.Active, await StatusAsync(shift.ShiftId));
        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal("ActorMismatch", AuditDb.String(AuditDb.Details(denied), "reason"));
    }

    [Fact]
    public async Task TACTOR_open_shift_for_someone_else_is_denied()
    {
        _db.RequireAvailable();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Shifts(sp).OpenShiftAsync(op.UserId, 0m));

        Assert.Equal("ActorMismatch", ex.Reason);
        Assert.Null(await Shifts(sp).GetActiveShiftAsync(op.UserId));
    }

    [Fact]
    public async Task TR15b_closing_a_non_active_shift_fails_without_SHIFT_CLOSE_row()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var shift = await Shifts(sp).OpenShiftAsync(op.UserId, 0m);
        await Shifts(sp).CloseShiftAsync(shift.ShiftId, op.UserId, 0m);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Shifts(sp).CloseShiftAsync(shift.ShiftId, op.UserId, 0m));

        Assert.DoesNotContain(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ShiftClose), r => r.Outcome == AuditOutcome.Success);
    }

    [Fact]
    public async Task User_without_Shift_Open_cannot_open_a_shift()
    {
        _db.RequireAvailable();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("noshift"), Permissions.ParkingView);
        var user = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Shifts(sp).OpenShiftAsync(user.UserId, 0m));

        Assert.Equal(new[] { Permissions.ShiftOpen }, ex.RequiredPermissions);
        Assert.Null(await Shifts(sp).GetActiveShiftAsync(user.UserId));
    }
}
