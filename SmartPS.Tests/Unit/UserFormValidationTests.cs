using SmartPS.Models.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Dashboard;
using SmartPS.ViewModels.UserManagement;

namespace SmartPS.Tests.Unit;

/// <summary>DA-3: kiểm tra ô nhập form Người dùng (sửa và tạo mới), nút Lưu khoá khi còn lỗi.</summary>
public class UserFormValidationTests
{
    private sealed class AllowAll : IPermissionService
    {
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => true;
        public bool HasAnyPermission(params string[] permissions) => true;
        public bool HasAllPermissions(params string[] permissions) => true;
        public bool IsAdmin() => true;
    }

    private static readonly Role Staff = new() { RoleId = 3, RoleName = "Staff" };

    private static EditUserViewModel CreateEdit()
    {
        var user = new User { UserId = 5, Username = "u5", FullName = "Nguyen Van A", RoleId = 3, IsActive = true };
        return new EditUserViewModel(user, new[] { Staff }, new FakeAuthService(new CurrentUserContext()),
            new FakeDialogService(), new FakeLocalizationService());
    }

    private static UserManagementViewModel CreateList()
        => new(new FakeAuthService(new CurrentUserContext()), new FakeDialogService(), new FakeLocalizationService(), new AllowAll());

    [Fact]
    public void Edit_valid_user_has_no_errors_and_save_enabled()
    {
        var vm = CreateEdit();
        Assert.False(vm.HasErrors);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Edit_empty_fullname_shows_error_and_locks_save_then_clears()
    {
        var vm = CreateEdit();
        vm.FullName = "  ";
        Assert.True(vm.HasErrors);
        Assert.Equal(new[] { AppText.Get("Msg_EditUser_RequiredFullName") }, vm.GetErrors(nameof(vm.FullName)).Cast<string>());
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.FullName = "Tran B";
        Assert.False(vm.HasErrors);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Edit_missing_role_is_error()
    {
        var vm = CreateEdit();
        vm.SelectedRole = null;
        Assert.True(vm.HasErrors);
        Assert.NotEmpty(vm.GetErrors(nameof(vm.SelectedRole)).Cast<string>());
        vm.SelectedRole = Staff;
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Edit_short_password_and_mismatch_are_errors_and_clear_when_fixed()
    {
        var vm = CreateEdit();
        vm.NewPassword = "123";
        Assert.Equal(new[] { AppText.Get("Msg_EditUser_PasswordMinLength") }, vm.GetErrors(nameof(vm.NewPassword)).Cast<string>());
        Assert.NotEmpty(vm.GetErrors(nameof(vm.ConfirmPassword)).Cast<string>());
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.NewPassword = "123456";
        vm.ConfirmPassword = "654321";
        Assert.Empty(vm.GetErrors(nameof(vm.NewPassword)).Cast<string>());
        Assert.Equal(new[] { AppText.Get("Msg_EditUser_PasswordMismatch") }, vm.GetErrors(nameof(vm.ConfirmPassword)).Cast<string>());

        vm.ConfirmPassword = "123456";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Edit_blank_passwords_are_not_validated()
    {
        var vm = CreateEdit();
        vm.NewPassword = "abcdefg";
        vm.ConfirmPassword = "abcdefg";
        vm.NewPassword = string.Empty;
        vm.ConfirmPassword = string.Empty;
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Create_form_errors_appear_per_field_and_disappear()
    {
        var vm = CreateList();
        Assert.False(vm.HasErrors);

        vm.ValidateCreateForm();
        Assert.True(vm.HasErrors);
        Assert.NotEmpty(vm.GetErrors(nameof(vm.NewUsername)).Cast<string>());
        Assert.NotEmpty(vm.GetErrors(nameof(vm.NewFullName)).Cast<string>());
        Assert.NotEmpty(vm.GetErrors(nameof(vm.NewPassword)).Cast<string>());
        Assert.NotEmpty(vm.GetErrors(nameof(vm.SelectedRole)).Cast<string>());
        Assert.False(vm.CreateUserCommand.CanExecute(null));

        vm.NewUsername = "abc";
        vm.NewFullName = "Ho Ten";
        vm.SelectedRole = Staff;
        vm.NewPassword = "12345";
        Assert.Equal(new[] { AppText.Get("Msg_CreateUser_PasswordMinLength") }, vm.GetErrors(nameof(vm.NewPassword)).Cast<string>());
        vm.NewPassword = "123456";
        Assert.NotEmpty(vm.GetErrors(nameof(vm.ConfirmPassword)).Cast<string>());
        vm.ConfirmPassword = "123456";
        Assert.False(vm.HasErrors);
        Assert.True(vm.CreateUserCommand.CanExecute(null));
    }
}
