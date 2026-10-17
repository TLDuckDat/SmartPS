using System.Text.Json;
using SmartPS.Models.Auth;

namespace SmartPS.Tests.Unit;

/// <summary>R5 (service-level enforcement), m6 (actor mismatch), m5 (DenyAsync with Role:Admin marker).</summary>
public class AuthorizationGuardTests
{
    private static (AuthorizationGuard Guard, FakeAuditService Audit, CurrentUserContext Context) Create(User? user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var audit = new FakeAuditService();
        var guard = new AuthorizationGuard(new PermissionService(context), context, audit);
        return (guard, audit, context);
    }

    private static JsonElement DetailsOf(AuditEntry entry)
    {
        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        return doc.RootElement.Clone();
    }

    private static string[] RequiredOf(AuditEntry entry)
        => DetailsOf(entry).GetProperty("requiredPermissions").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task DemandAsync_with_permission_passes_without_audit()
    {
        var (guard, audit, _) = Create(TestUsers.Operator());

        await guard.DemandAsync(Permissions.ParkingCheckIn);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task DemandAsync_missing_permission_logs_ACCESS_DENIED_then_throws()
    {
        // AC-3 (unit part): missing User.Create → PermissionDeniedException + one ACCESS_DENIED/Denied entry.
        var user = TestUsers.Operator();
        var (guard, audit, _) = Create(user);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => guard.DemandAsync(Permissions.UserCreate, "User", "15"));

        Assert.IsAssignableFrom<UnauthorizedAccessException>(ex);
        Assert.Equal(new[] { Permissions.UserCreate }, ex.RequiredPermissions);
        Assert.Equal("MissingPermission", ex.Reason);
        Assert.Equal(user.Username, ex.Username);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal("User", entry.EntityType);
        Assert.Equal("15", entry.EntityId);
        Assert.Equal(new[] { Permissions.UserCreate }, RequiredOf(entry));
        Assert.Equal("MissingPermission", DetailsOf(entry).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task DemandAsync_when_not_logged_in_is_denied_as_NotAuthenticated()
    {
        // R4: not logged in = no permission at all.
        var (guard, audit, _) = Create(null);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => guard.DemandAsync(Permissions.ParkingView));

        Assert.Equal("NotAuthenticated", ex.Reason);
        Assert.Null(ex.Username);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
    }

    [Fact]
    public async Task DemandAsync_system_admin_passes_without_explicit_grant()
    {
        var (guard, audit, _) = Create(TestUsers.AdminWithoutGrants());

        await guard.DemandAsync(Permissions.AuditVerify);
        await guard.DemandAsync(Permissions.RoleManage);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task DemandAnyAsync_passes_when_one_permission_is_held()
    {
        var (guard, audit, _) = Create(TestUsers.Build("Exit", Permissions.ParkingCheckOut));

        await guard.DemandAnyAsync(new[] { Permissions.ParkingCheckIn, Permissions.ParkingCheckOut });

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task DemandAnyAsync_denies_with_all_required_permissions_listed()
    {
        var (guard, audit, _) = Create(TestUsers.Build("Viewer", Permissions.ParkingView));

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            guard.DemandAnyAsync(new[] { Permissions.ParkingCheckIn, Permissions.ParkingCheckOut }, "Navigation", "GateControl"));

        Assert.Equal(new[] { Permissions.ParkingCheckIn, Permissions.ParkingCheckOut }, ex.RequiredPermissions);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(new[] { Permissions.ParkingCheckIn, Permissions.ParkingCheckOut }, RequiredOf(entry));
        Assert.Equal("Navigation", entry.EntityType);
        Assert.Equal("GateControl", entry.EntityId);
    }

    [Fact]
    public async Task DemandActorAsync_matching_actor_with_permission_passes()
    {
        var user = TestUsers.Operator();
        var (guard, audit, _) = Create(user);

        await guard.DemandActorAsync(user.UserId, Permissions.ShiftOpen);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task DemandActorAsync_mismatched_actor_is_denied_even_for_admin()
    {
        // m6: the actor id passed by the caller must be the logged-in user.
        var user = TestUsers.AdminWithoutGrants();
        var (guard, audit, _) = Create(user);
        var otherId = user.UserId + 1000;

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            guard.DemandActorAsync(otherId, Permissions.ShiftClose, "Shift", "5"));

        Assert.Equal("ActorMismatch", ex.Reason);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        var details = DetailsOf(entry);
        Assert.Equal("ActorMismatch", details.GetProperty("reason").GetString());
        Assert.Equal(otherId, details.GetProperty("actorUserIdRequested").GetInt32());
    }

    [Fact]
    public async Task DemandActorAsync_without_logged_in_user_is_ActorMismatch()
    {
        var (guard, audit, _) = Create(null);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => guard.DemandActorAsync(1, Permissions.ShiftOpen));

        Assert.Equal("ActorMismatch", ex.Reason);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task DemandActorAsync_matching_actor_without_permission_is_MissingPermission()
    {
        var user = TestUsers.Operator();
        var (guard, audit, _) = Create(user);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => guard.DemandActorAsync(user.UserId, Permissions.ShiftReview));

        Assert.Equal("MissingPermission", ex.Reason);
        Assert.Equal(new[] { Permissions.ShiftReview }, ex.RequiredPermissions);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task DenyAsync_always_logs_and_throws_with_given_reason()
    {
        // m5: non-admin assigning the Admin role → DenyAsync([Role:Admin], "AdminRoleRequired").
        var user = TestUsers.Build("Helper", Permissions.UserCreate, Permissions.UserEdit);
        var (guard, audit, _) = Create(user);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            guard.DenyAsync(new[] { SystemRoles.AdminRoleRequirement }, "AdminRoleRequired", "User", "7"));

        Assert.Equal("AdminRoleRequired", ex.Reason);
        Assert.Equal(new[] { "Role:Admin" }, ex.RequiredPermissions);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(new[] { "Role:Admin" }, RequiredOf(entry));
        Assert.Equal("AdminRoleRequired", DetailsOf(entry).GetProperty("reason").GetString());
        Assert.Equal("User", entry.EntityType);
        Assert.Equal("7", entry.EntityId);
    }

    [Fact]
    public void PermissionDeniedException_exposes_contract_properties()
    {
        var ex = new PermissionDeniedException(new[] { "A.B", "C.D" }, "bob");

        Assert.IsAssignableFrom<UnauthorizedAccessException>(ex);
        Assert.Equal(new[] { "A.B", "C.D" }, ex.RequiredPermissions);
        Assert.Equal("bob", ex.Username);
        Assert.Equal("MissingPermission", ex.Reason);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }
}
