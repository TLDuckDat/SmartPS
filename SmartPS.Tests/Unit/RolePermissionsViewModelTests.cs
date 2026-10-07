using SmartPS.Models.Auth;
using SmartPS.Services.RolePermissions;
using SmartPS.ViewModels.RolePermissions;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Fix round 1 — FX2 (spec §6.10): the matrix screen is read-only for everyone except a system Admin, even with Role.Manage.
/// FX10: saving sends only the roles whose cells changed.
/// </summary>
public class RolePermissionsViewModelTests
{
    private const int AdminRoleId = 1, ManagerRoleId = 2, OperatorRoleId = 3;

    private sealed class FakeRolePermissionService : IRolePermissionService
    {
        public List<IReadOnlyDictionary<int, IReadOnlyCollection<string>>> Saves { get; } = new();

        public TaskCompletionSource SaveReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RolePermissionMatrix> GetMatrixAsync(CancellationToken cancellationToken = default)
        {
            var roles = new[]
            {
                new RoleInfo(AdminRoleId, "Admin", true),
                new RoleInfo(ManagerRoleId, "Manager", false),
                new RoleInfo(OperatorRoleId, "Operator", false)
            };
            var permissions = new[] { Permissions.ParkingView, Permissions.ReportView, Permissions.ReportExport }
                .Select((p, i) => new PermissionInfo(i + 1, p, Permissions.GetModule(p), p))
                .ToList();
            var grants = new Dictionary<int, IReadOnlySet<string>>
            {
                [AdminRoleId] = permissions.Select(p => p.Name).ToHashSet(),
                [ManagerRoleId] = new HashSet<string> { Permissions.ParkingView, Permissions.ReportView, Permissions.ReportExport },
                [OperatorRoleId] = new HashSet<string> { Permissions.ParkingView, Permissions.ReportView }
            };
            return Task.FromResult(new RolePermissionMatrix(roles, permissions, grants));
        }

        public Task<IReadOnlyList<RolePermissionChange>> SaveAsync(
            IReadOnlyDictionary<int, IReadOnlyCollection<string>> desiredGrantsByRoleId,
            CancellationToken cancellationToken = default)
        {
            Saves.Add(desiredGrantsByRoleId);
            SaveReceived.TrySetResult();
            return Task.FromResult<IReadOnlyList<RolePermissionChange>>(
                new[] { new RolePermissionChange(OperatorRoleId, "Operator", Array.Empty<string>(), new[] { Permissions.ReportView }) });
        }
    }

    private static async Task<(RolePermissionsViewModel Vm, FakeRolePermissionService Service)> CreateLoadedAsync(User user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var service = new FakeRolePermissionService();
        var vm = new RolePermissionsViewModel(service, new PermissionService(context), new FakeDialogService(), new FakeLocalizationService());
        await vm.LoadDataAsync();
        return (vm, service);
    }

    [Fact]
    public async Task FX2_non_admin_with_Role_Manage_sees_a_read_only_matrix()
    {
        var (vm, _) = await CreateLoadedAsync(TestUsers.Build("Auditor", Permissions.RoleView, Permissions.RoleManage));

        Assert.False(vm.CanEdit);
        Assert.True(vm.IsReadOnly);
        Assert.NotEmpty(vm.Rows);
        Assert.All(vm.Rows.SelectMany(r => r.Cells), c => Assert.False(c.IsEditable));
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task System_admin_can_edit_every_non_admin_column()
    {
        var (vm, _) = await CreateLoadedAsync(TestUsers.AdminWithoutGrants());

        Assert.True(vm.CanEdit);
        Assert.False(vm.IsReadOnly);
        Assert.All(vm.Rows.SelectMany(r => r.Cells), c => Assert.Equal(!c.Role.IsSystemAdmin, c.IsEditable));
    }

    [Fact]
    public async Task FX10_save_sends_only_the_roles_whose_cells_changed()
    {
        var (vm, service) = await CreateLoadedAsync(TestUsers.AdminWithoutGrants());
        var cell = vm.Rows.Single(r => r.Name == Permissions.ReportView).Cells.Single(c => c.Role.RoleId == OperatorRoleId);
        cell.IsGranted = false;
        Assert.True(vm.HasChanges);

        vm.SaveCommand.Execute(null);
        await service.SaveReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var desired = Assert.Single(service.Saves);
        Assert.Equal(new[] { OperatorRoleId }, desired.Keys.ToArray());
        Assert.Equal(new HashSet<string> { Permissions.ParkingView }, desired[OperatorRoleId].ToHashSet());
    }
}
