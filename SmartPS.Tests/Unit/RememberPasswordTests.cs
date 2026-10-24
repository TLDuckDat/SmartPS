using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.ViewModels.Auth;

namespace SmartPS.Tests.Unit;

/// <summary>
/// "Ghi nhớ mật khẩu" trên màn đăng nhập: mật khẩu được mã hoá DPAPI (không lưu dạng rõ), chỉ lưu sau khi đăng nhập
/// thành công, bỏ chọn thì xoá, tệp hỏng không làm hỏng màn đăng nhập.
/// </summary>
public sealed class RememberPasswordTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "SmartPS.Tests", "remember-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "credentials.dat");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private CredentialStore NewStore() => new(FilePath);

    private sealed class Harness
    {
        public required LoginViewModel ViewModel { get; init; }
        public required FakeAuthService Auth { get; init; }
        public required FakeDialogService Dialog { get; init; }
    }

    private Harness NewLogin(bool loginSucceeds = true)
    {
        var context = new CurrentUserContext();
        var auth = new FakeAuthService(context)
        {
            LoginHandler = request => loginSucceeds ? TestUsers.Build("Operator", "Parking.View") : null
        };
        var dialog = new FakeDialogService();
        var vm = new LoginViewModel(auth, dialog, new FakeLocalizationService(), NewStore());
        return new Harness { ViewModel = vm, Auth = auth, Dialog = dialog };
    }

    [Fact]
    public void Store_round_trips_and_the_file_does_not_contain_the_plain_password()
    {
        var store = NewStore();

        store.Save("  operator01 ", "Secret#Pass9");

        Assert.Equal(new SavedCredentials("operator01", "Secret#Pass9"), store.Load());
        Assert.DoesNotContain("Secret#Pass9", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_removes_the_saved_credentials()
    {
        var store = NewStore();
        store.Save("operator01", "Secret#Pass9");

        store.Clear();

        Assert.Null(store.Load());
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Username\":\"admin\",\"ProtectedPassword\":\"bm90LWVuY3J5cHRlZA==\"}")]
    [InlineData("{\"Username\":\"admin\",\"ProtectedPassword\":\"%%%\"}")]
    public void Unreadable_file_is_ignored_and_deleted(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, content);

        Assert.Null(NewStore().Load());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Login_screen_starts_empty_and_unchecked_without_saved_credentials()
    {
        var h = NewLogin();

        Assert.Equal(string.Empty, h.ViewModel.Username);
        Assert.Equal(string.Empty, h.ViewModel.Password);
        Assert.False(h.ViewModel.RememberPassword);
    }

    [Fact]
    public void Successful_login_with_remember_checked_saves_and_prefills_the_next_login_screen()
    {
        var first = NewLogin();
        first.ViewModel.Username = "operator01";
        first.ViewModel.Password = "Secret#Pass9";
        first.ViewModel.RememberPassword = true;

        first.ViewModel.LoginCommand.Execute(null);

        var next = NewLogin();
        Assert.Equal("operator01", next.ViewModel.Username);
        Assert.Equal("Secret#Pass9", next.ViewModel.Password);
        Assert.True(next.ViewModel.RememberPassword);
    }

    [Fact]
    public void Successful_login_with_remember_unchecked_forgets_saved_credentials()
    {
        NewStore().Save("operator01", "Old#Pass1");
        var h = NewLogin();
        Assert.True(h.ViewModel.RememberPassword);

        h.ViewModel.RememberPassword = false;
        h.ViewModel.LoginCommand.Execute(null);

        Assert.Null(NewStore().Load());
    }

    [Fact]
    public void Failed_login_does_not_save_anything()
    {
        var h = NewLogin(loginSucceeds: false);
        h.ViewModel.Username = "operator01";
        h.ViewModel.Password = "Wrong#Pass1";
        h.ViewModel.RememberPassword = true;

        h.ViewModel.LoginCommand.Execute(null);

        Assert.Null(NewStore().Load());
        Assert.NotEmpty(h.Dialog.Warnings);
    }
}
