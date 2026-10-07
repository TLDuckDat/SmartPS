using Microsoft.Extensions.DependencyInjection;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Payment;
using SmartPS.Models.Shifts;
using SmartPS.Models.Parking;
using SmartPS.Services.Payment;
using SmartPS.Services.RolePermissions;
using SmartPS.Services.Shifts;

namespace SmartPS.Tests.Integration;

/// <summary>
/// T-R14: table-driven proof that every action of R14 is produced by its real operation with the expected
/// outcome, entity type and detail keys (plan §2.6/§2.7), and that no secret leaks into the row (R18).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AuditEventsCatalogTests : IClassFixture<PostgresDatabaseFixture>
{
    private sealed record Expectation(AuditOutcome Outcome, string? EntityType, string[] Keys);

    private static readonly IReadOnlyDictionary<string, Expectation> Catalogue = new Dictionary<string, Expectation>
    {
        [AuditActions.AuthLoginSuccess] = new(AuditOutcome.Success, null, Array.Empty<string>()),
        [AuditActions.AuthLoginFailed] = new(AuditOutcome.Failed, null, new[] { "attemptedUsername", "reason" }),
        [AuditActions.AuthLogout] = new(AuditOutcome.Success, null, Array.Empty<string>()),
        [AuditActions.AccessDenied] = new(AuditOutcome.Denied, null, new[] { "requiredPermissions", "reason" }),
        [AuditActions.UserCreate] = new(AuditOutcome.Success, "User", new[] { "username", "fullName", "roleId", "roleName", "isActive" }),
        [AuditActions.UserUpdate] = new(AuditOutcome.Success, "User", new[] { "before", "after", "passwordChanged" }),
        [AuditActions.UserDelete] = new(AuditOutcome.Success, "User", new[] { "username", "fullName", "roleName" }),
        [AuditActions.RolePermissionsUpdate] = new(AuditOutcome.Success, "Role", new[] { "roleName", "added", "removed" }),
        [AuditActions.ParkingCheckIn] = new(AuditOutcome.Success, null, new[] { "licensePlate", "ticketCode", "vehicleTypeId", "slotCode", "isMonthlyPass" }),
        [AuditActions.ParkingCheckOut] = new(AuditOutcome.Success, "ParkingSession", new[] { "sessionId", "licensePlate", "ticketCode", "fee", "paymentMethod", "shiftId" }),
        [AuditActions.PaymentRefund] = new(AuditOutcome.Success, null, new[] { "paymentId", "sessionId", "amount", "paymentMethod", "reason", "transactionReference" }),
        [AuditActions.PaymentCancel] = new(AuditOutcome.Success, null, new[] { "paymentId", "amount", "previousStatus", "byOwner" }),
        [AuditActions.ShiftOpen] = new(AuditOutcome.Success, "Shift", new[] { "beginningCash" }),
        [AuditActions.ShiftClose] = new(AuditOutcome.Success, "Shift", new[] { "expectedCash", "actualCash", "difference" }),
        [AuditActions.ShiftReview] = new(AuditOutcome.Success, "Shift", new[] { "note" }),
        [AuditActions.ShiftAdjustment] = new(AuditOutcome.Success, "FinancialTransaction", new[] { "shiftId", "type", "paymentMethod", "amount", "transactionCode" }),
        [AuditActions.AuditVerify] = new(AuditOutcome.Success, null, Array.Empty<string>()),
        [AuditActions.ReportExport] = new(AuditOutcome.Success, "Report", new[] { "from", "to", "customerGroup", "rowCounts", "fileName" }),
    };

    private readonly PostgresDatabaseFixture _db;

    public AuditEventsCatalogTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    public static TheoryData<string> Actions()
    {
        var data = new TheoryData<string>();
        // Resident/visitor actions are covered by ResidentVisitorAuditCatalogTests.
        foreach (var action in AuditActions.All.Except(ResidentVisitorAuditActions.All))
        {
            data.Add(action);
        }

        return data;
    }

    [Fact]
    public void Catalogue_covers_every_R14_action()
    {
        Assert.Equal(
            AuditActions.All.Except(ResidentVisitorAuditActions.All).OrderBy(a => a, StringComparer.Ordinal),
            Catalogue.Keys.OrderBy(a => a, StringComparer.Ordinal));
        Assert.Equal(18, Catalogue.Count);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task TR14_operation_produces_expected_audit_row(string action)
    {
        _db.RequireAvailable();
        var expectation = Catalogue[action];

        var (idBefore, secrets) = await ProduceAsync(action);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, action);
        var matching = rows.Where(r => r.Outcome == expectation.Outcome).ToList();
        Assert.True(matching.Count >= 1, $"no {action}/{expectation.Outcome} row was written (found {rows.Count} {action} rows)");
        var row = matching[^1];
        if (expectation.EntityType is not null)
        {
            Assert.Equal(expectation.EntityType, row.EntityType);
            Assert.False(string.IsNullOrWhiteSpace(row.EntityId));
        }

        AuditDb.HasKeys(row, expectation.Keys);
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);

        var allNew = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        Assert.All(allNew, r =>
        {
            Assert.DoesNotContain("$2a$", r.Details, StringComparison.Ordinal);
            Assert.DoesNotContain("$2b$", r.Details, StringComparison.Ordinal);
            Assert.DoesNotContain("passwordHash", r.Details, StringComparison.OrdinalIgnoreCase);
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, r.Details, StringComparison.Ordinal);
            }
        });

        if (action == AuditActions.UserUpdate)
        {
            Assert.True(AuditDb.Details(row).GetProperty("passwordChanged").GetBoolean());
        }
    }

    /// <summary>Runs the producing operation; returns the audit id watermark taken just before it and secrets that must not appear.</summary>
    private async Task<(long IdBefore, string[] Secrets)> ProduceAsync(string action)
    {
        using var sp = IntegrationServices.Create(_db);
        var factory = _db.Factory;
        Task<long> Mark() => AuditDb.MaxIdAsync(factory);

        switch (action)
        {
            case AuditActions.AuthLoginSuccess:
            {
                var user = await TestUsers.CreateAsync(factory, "Operator");
                var id = await Mark();
                await sp.LoginAsync(user.Username);
                return (id, new[] { TestUsers.DefaultPassword });
            }

            case AuditActions.AuthLoginFailed:
            {
                var user = await TestUsers.CreateAsync(factory, "Operator");
                var id = await Mark();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    sp.Auth().LoginAsync(new LoginRequest { Username = user.Username, Password = "Typed#Wrong1" }));
                return (id, new[] { "Typed#Wrong1" });
            }

            case AuditActions.AuthLogout:
            {
                var user = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(user.Username);
                var id = await Mark();
                await sp.Auth().LogoutAsync();
                return (id, Array.Empty<string>());
            }

            case AuditActions.AccessDenied:
            {
                var user = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(user.Username);
                var id = await Mark();
                await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().RegisterAsync(new RegisterRequest
                {
                    Username = TestUsers.UniqueName("denied"), Password = "Denied#123", FullName = "x", RoleId = user.RoleId
                }));
                return (id, new[] { "Denied#123" });
            }

            case AuditActions.UserCreate:
            {
                await sp.LoginAdminAsync();
                var id = await Mark();
                Assert.True(await sp.Auth().RegisterAsync(new RegisterRequest
                {
                    Username = TestUsers.UniqueName("cat"), Password = "Create#123", FullName = "Catalogue",
                    RoleId = await TestUsers.RoleIdAsync(factory, "Operator")
                }));
                return (id, new[] { "Create#123" });
            }

            case AuditActions.UserUpdate:
            {
                var target = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAdminAsync();
                var id = await Mark();
                Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest
                {
                    UserId = target.UserId, FullName = "Updated", RoleId = target.RoleId, IsActive = true, NewPassword = "Changed#456"
                }));
                return (id, new[] { "Changed#456" });
            }

            case AuditActions.UserDelete:
            {
                var target = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAdminAsync();
                var id = await Mark();
                Assert.True(await sp.Auth().DeleteUserAsync(target.UserId));
                return (id, Array.Empty<string>());
            }

            case AuditActions.RolePermissionsUpdate:
            {
                var role = await TestUsers.CreateRoleAsync(factory, TestUsers.UniqueName("cat"), Permissions.ParkingView);
                await sp.LoginAdminAsync();
                var id = await Mark();
                var changes = await sp.GetRequiredService<IRolePermissionService>().SaveAsync(
                    new Dictionary<int, IReadOnlyCollection<string>> { [role.RoleId] = new[] { Permissions.ParkingView, Permissions.ReportView } });
                Assert.Single(changes);
                return (id, Array.Empty<string>());
            }

            case AuditActions.ParkingCheckIn:
            {
                var op = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(op.Username);
                var id = await Mark();
                await ParkingFlows.CheckInAsync(sp, ParkingFlows.UniquePlate());
                return (id, Array.Empty<string>());
            }

            case AuditActions.ParkingCheckOut:
            {
                var op = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(op.Username);
                await ParkingFlows.OpenShiftAsync(sp, op.UserId);
                var plate = ParkingFlows.UniquePlate();
                await ParkingFlows.CheckInAsync(sp, plate);
                var id = await Mark();
                var (_, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, op.UserId);
                Assert.True(result.Success, result.Message);
                return (id, Array.Empty<string>());
            }

            case AuditActions.PaymentRefund:
            {
                var op = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(op.Username);
                var (_, paymentId, _) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
                Assert.True((await ParkingFlows.SendPaidWebhookAsync(sp, paymentId)).PaymentPaid);
                await sp.Auth().LogoutAsync();
                var manager = await TestUsers.CreateAsync(factory, "Manager");
                await sp.LoginAsync(manager.Username);
                await ParkingFlows.OpenShiftAsync(sp, manager.UserId);
                var id = await Mark();
                var refund = await sp.GetRequiredService<IPaymentService>().ConfirmManualRefundAsync(paymentId, "catalogue", manager.UserId);
                Assert.True(refund.Success, refund.Message);
                return (id, Array.Empty<string>());
            }

            case AuditActions.PaymentCancel:
            {
                var op = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(op.Username);
                var (_, paymentId, _) = await ParkingFlows.PendingVietQrAsync(sp, op.UserId);
                var id = await Mark();
                var cancel = await sp.GetRequiredService<IPaymentService>().CancelPaymentAsync(paymentId);
                Assert.True(cancel.Success, cancel.Message);
                Assert.Equal(PaymentStatus.Cancelled, cancel.Status);
                return (id, Array.Empty<string>());
            }

            case AuditActions.ShiftOpen:
            {
                var op = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(op.Username);
                var id = await Mark();
                await ParkingFlows.OpenShiftAsync(sp, op.UserId);
                return (id, Array.Empty<string>());
            }

            case AuditActions.ShiftClose:
            {
                var op = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(op.Username);
                var shift = await ParkingFlows.OpenShiftAsync(sp, op.UserId);
                var id = await Mark();
                await sp.GetRequiredService<IShiftService>().CloseShiftAsync(shift.ShiftId, op.UserId, shift.BeginningCash);
                return (id, Array.Empty<string>());
            }

            case AuditActions.ShiftReview:
            case AuditActions.ShiftAdjustment:
            {
                var op = await TestUsers.CreateAsync(factory, "Operator");
                await sp.LoginAsync(op.Username);
                var shift = await ParkingFlows.OpenShiftAsync(sp, op.UserId);
                await sp.GetRequiredService<IShiftService>().CloseShiftAsync(shift.ShiftId, op.UserId, shift.BeginningCash);
                await sp.Auth().LogoutAsync();
                var manager = await TestUsers.CreateAsync(factory, "Manager");
                await sp.LoginAsync(manager.Username);
                var id = await Mark();
                if (action == AuditActions.ShiftReview)
                {
                    await sp.GetRequiredService<IShiftService>().ReviewShiftAsync(shift.ShiftId, manager.UserId, "catalogue");
                }
                else
                {
                    await sp.GetRequiredService<IShiftService>().CreateManualTransactionAsync(
                        shift.ShiftId, manager.UserId, FinancialTransactionType.Adjustment, PaymentMethod.Cash, 2_000m, null, "catalogue");
                }

                return (id, Array.Empty<string>());
            }

            case AuditActions.AuditVerify:
            {
                await sp.LoginAdminAsync();
                var id = await Mark();
                Assert.True((await sp.GetRequiredService<IAuditIntegrityVerifier>().VerifyAsync()).IsValid);
                return (id, Array.Empty<string>());
            }

            case AuditActions.ReportExport:
            {
                await sp.LoginAdminAsync();
                var tempDir = Directory.CreateTempSubdirectory("smartps-catalog-").FullName;
                var range = ReportPeriodCalculator.Resolve(ReportPeriodPreset.Last7Days, ReportPeriodCalculator.TodayVn(DateTime.UtcNow));
                var id = await Mark();
                var export = await sp.GetRequiredService<IReportExportService>().ExportAsync(
                    new ReportFilter(range, Preset: ReportPeriodPreset.Last7Days), Path.Combine(tempDir, "catalog.xlsx"));
                Assert.Equal(ReportExportStatus.Success, export.Status);
                return (id, new[] { tempDir });
            }

            default:
                throw new InvalidOperationException($"No producing scenario for {action}; extend the catalogue.");
        }
    }
}
