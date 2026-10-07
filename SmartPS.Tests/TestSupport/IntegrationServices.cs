using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Customers;
using SmartPS.Services.GateControl;
using SmartPS.Services.Localization;
using SmartPS.Services.Payment;
using SmartPS.Services.Payment.Mock;
using SmartPS.Services.ParkingZones;
using SmartPS.Services.RolePermissions;
using SmartPS.Services.Shifts;
using SmartPS.Services.Storage;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Builds a DI container that mirrors App.xaml.cs (business services only, no WPF windows)
/// against the fixture database. Each call returns an independent container, i.e. an
/// independent "logged-in session" (ICurrentUserContext is a singleton per container).
/// </summary>
public static class IntegrationServices
{
    public static ServiceProvider Create(PostgresDatabaseFixture fixture, Action<IServiceCollection>? overrides = null)
    {
        var services = new ServiceCollection();

        services.AddDbContextFactory<SmartPsDbContext>(options => options.UseNpgsql(fixture.ConnectionString));

        services.AddSingleton<ICurrentUserContext, CurrentUserContext>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IAuthorizationGuard, AuthorizationGuard>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IRolePermissionService, RolePermissionService>();
        services.AddSingleton<IAuditQueryService, AuditQueryService>();
        services.AddSingleton<IAuditIntegrityVerifier, AuditIntegrityVerifier>();

        services.AddSingleton<IShiftService, ShiftService>();
        services.AddSingleton<IParkingFeeCalculator, StandardParkingFeeCalculator>();
        services.AddSingleton<IImageStorageService, ImageStorageService>();
        services.AddSingleton<IGateControlService, GateControlService>();
        services.AddSingleton<MockPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<MockPaymentGateway>());
        services.AddSingleton<IPaymentService, PaymentService>();

        // Resident/visitor flow (C7, C9)
        services.AddSingleton<ICustomerService, CustomerService>();
        services.AddSingleton<IMonthlyTicketService, MonthlyTicketService>();
        services.AddSingleton<IBlacklistService, BlacklistService>();
        services.AddSingleton<IParkingZoneService, ParkingZoneService>();

        // Reporting & BI
        services.TryAddSingleton<ILocalizationService, FakeLocalizationService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IReportExportService, ReportExportService>();

        overrides?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>Replaces IAuditService with a decorator that fails AppendAsync for <paramref name="failingAction"/>.</summary>
    public static Action<IServiceCollection> FailAppendFor(string failingAction) => services =>
    {
        services.AddSingleton<AuditService>();
        services.AddSingleton<ThrowingAuditServiceDecorator>(sp =>
            new ThrowingAuditServiceDecorator(sp.GetRequiredService<AuditService>(), failingAction));
        services.AddSingleton<IAuditService>(sp => sp.GetRequiredService<ThrowingAuditServiceDecorator>());
    };

    /// <summary>Replaces IAuditService with <see cref="HookAuditServiceDecorator"/> running <paramref name="beforeBegin"/> before every Begin.</summary>
    public static Action<IServiceCollection> HookBeforeBegin(Action beforeBegin) => services =>
    {
        services.AddSingleton<AuditService>();
        services.AddSingleton<HookAuditServiceDecorator>(sp =>
            new HookAuditServiceDecorator(sp.GetRequiredService<AuditService>(), beforeBegin));
        services.AddSingleton<IAuditService>(sp => sp.GetRequiredService<HookAuditServiceDecorator>());
    };

    public static IDbContextFactory<SmartPsDbContext> DbFactory(this IServiceProvider sp)
        => sp.GetRequiredService<IDbContextFactory<SmartPsDbContext>>();

    public static IAuthService Auth(this IServiceProvider sp) => sp.GetRequiredService<IAuthService>();

    public static IPermissionService Perms(this IServiceProvider sp) => sp.GetRequiredService<IPermissionService>();

    public static async Task<User> LoginAsync(this IServiceProvider sp, string username, string password = TestUsers.DefaultPassword)
    {
        var user = await sp.Auth().LoginAsync(new LoginRequest { Username = username, Password = password });
        Assert.NotNull(user);
        return user!;
    }

    public static Task<User> LoginAdminAsync(this IServiceProvider sp)
        => sp.LoginAsync(TestUsers.SeedAdminUsername, TestUsers.SeedAdminPassword);
}
