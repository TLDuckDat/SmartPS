using SmartPS.Models.Auth;

namespace SmartPS.Tests.Unit;

/// <summary>R1, R4, AC-2: permission checks are based on the loaded permission snapshot, never on role names (except system Admin).</summary>
public class PermissionServiceTests
{
    private static (CurrentUserContext Context, PermissionService Service) Create(User? user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        return (context, new PermissionService(context));
    }

    public static TheoryData<string> AllPermissions()
    {
        var data = new TheoryData<string>();
        foreach (var p in Permissions.GetAll())
        {
            data.Add(p);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPermissions))]
    public void Null_user_has_no_permission(string permission)
    {
        // AC-2: Given CurrentUser == null, When HasPermission(x), Then false.
        var (_, service) = Create(null);

        Assert.False(service.HasPermission(permission));
    }

    [Fact]
    public void Null_user_is_not_authenticated_not_admin_and_has_no_any_or_all()
    {
        var (_, service) = Create(null);

        Assert.False(service.IsAuthenticated);
        Assert.False(service.IsAdmin());
        Assert.False(service.HasAnyPermission(Permissions.GetAll().ToArray()));
        Assert.False(service.HasAllPermissions(Permissions.ParkingView));
        Assert.False(service.HasAllPermissions());
    }

    [Fact]
    public void Operator_has_exactly_the_seed_grants()
    {
        var (_, service) = Create(TestUsers.Operator());

        Assert.True(service.IsAuthenticated);
        Assert.False(service.IsAdmin());
        foreach (var p in Permissions.GetAll())
        {
            Assert.Equal(TestUsers.OperatorSeedPermissions.Contains(p), service.HasPermission(p));
        }
    }

    [Fact]
    public void Permission_names_are_case_sensitive()
    {
        var (_, service) = Create(TestUsers.Operator());

        Assert.False(service.HasPermission("parking.view"));
        Assert.True(service.HasPermission("Parking.View"));
    }

    [Fact]
    public void Unknown_permission_is_denied_for_non_admin()
    {
        var (_, service) = Create(TestUsers.Manager());

        Assert.False(service.HasPermission("Nope.Nothing"));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    public void System_admin_role_has_every_permission_without_grants(string roleName)
    {
        // R1: Admin is a system role with all permissions (even ones not granted in RolePermissions).
        var (context, service) = Create(TestUsers.Build(roleName));

        Assert.True(context.IsSystemAdmin);
        Assert.True(service.IsAdmin());
        foreach (var p in Permissions.GetAll())
        {
            Assert.True(service.HasPermission(p), p);
        }

        Assert.True(service.HasAllPermissions(Permissions.GetAll().ToArray()));
    }

    [Fact]
    public void Role_named_like_admin_but_different_is_not_system_admin()
    {
        var (context, service) = Create(TestUsers.Build("Administrator", Permissions.UserView));

        Assert.False(context.IsSystemAdmin);
        Assert.False(service.IsAdmin());
        Assert.False(service.HasPermission(Permissions.UserCreate));
        Assert.True(service.HasPermission(Permissions.UserView));
    }

    [Fact]
    public void HasAny_and_HasAll_follow_set_semantics()
    {
        var (_, service) = Create(TestUsers.Build("Gate", Permissions.ParkingCheckOut));

        Assert.True(service.HasAnyPermission(Permissions.ParkingCheckIn, Permissions.ParkingCheckOut));
        Assert.False(service.HasAnyPermission(Permissions.ParkingCheckIn, Permissions.UserView));
        Assert.False(service.HasAllPermissions(Permissions.ParkingCheckIn, Permissions.ParkingCheckOut));
        Assert.True(service.HasAllPermissions(Permissions.ParkingCheckOut));
    }

    [Fact]
    public void User_without_role_is_authenticated_but_has_no_permissions()
    {
        var user = TestUsers.Operator();
        user.Role = null!;

        var (_, service) = Create(user);

        Assert.True(service.IsAuthenticated);
        Assert.False(service.HasPermission(Permissions.ParkingView));
        Assert.False(service.IsAdmin());
    }

    [Fact]
    public void Context_snapshot_follows_SetUser_and_raises_Changed()
    {
        var context = new CurrentUserContext();
        var service = new PermissionService(context);
        var raised = 0;
        context.Changed += (_, _) => raised++;

        context.SetUser(TestUsers.Manager());
        Assert.True(service.HasPermission(Permissions.AuditView));
        Assert.Contains(Permissions.AuditView, context.Permissions);

        context.SetUser(TestUsers.Operator());
        Assert.False(service.HasPermission(Permissions.AuditView));

        context.SetUser(null);
        Assert.Null(context.User);
        Assert.Empty(context.Permissions);
        Assert.False(context.IsSystemAdmin);
        Assert.False(service.HasPermission(Permissions.ParkingView));

        Assert.Equal(3, raised);
    }

    [Fact]
    public void Snapshot_is_not_affected_by_later_mutation_of_the_user_graph()
    {
        // The context holds an immutable permission snapshot (plan §1.1).
        var user = TestUsers.Operator();
        var context = new CurrentUserContext();
        context.SetUser(user);
        var service = new PermissionService(context);

        user.Role.RolePermissions.Clear();

        Assert.True(service.HasPermission(Permissions.ParkingView));
    }

    [Fact]
    public void SystemRoles_IsSystemAdmin_is_case_insensitive_and_null_safe()
    {
        Assert.True(SystemRoles.IsSystemAdmin("Admin"));
        Assert.True(SystemRoles.IsSystemAdmin("aDmIn"));
        Assert.False(SystemRoles.IsSystemAdmin("Manager"));
        Assert.False(SystemRoles.IsSystemAdmin(""));
        Assert.False(SystemRoles.IsSystemAdmin(null));
        Assert.Equal("Admin", SystemRoles.Admin);
        Assert.Equal("Role:Admin", SystemRoles.AdminRoleRequirement);
    }
}
