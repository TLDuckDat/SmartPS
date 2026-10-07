using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.Customers;
using SmartPS.Services.GateControl;
using SmartPS.Services.ParkingZones;
using SmartPS.Services.Shifts;

namespace SmartPS.Tests.Integration;

/// <summary>
/// R9 / plan §1.8: each of the 13 resident/visitor audit actions is produced by its real operation with the expected
/// outcome, entity type and detail keys, inside the hash chain, and with no identity card or raw phone (m7).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ResidentVisitorAuditCatalogTests : IClassFixture<PostgresDatabaseFixture>
{
    private sealed record Expectation(AuditOutcome Outcome, string EntityType, string[] Keys);

    private static readonly IReadOnlyDictionary<string, Expectation> Catalogue = new Dictionary<string, Expectation>
    {
        [AuditActions.CustomerCreate] = new(AuditOutcome.Success, "Customer", new[] { "fullName", "phoneNumber", "isResident", "apartmentCode", "building", "type", "hasEmail", "hasIdentityCard" }),
        [AuditActions.CustomerUpdate] = new(AuditOutcome.Success, "Customer", new[] { "before", "after", "emailChanged", "identityCardChanged", "notesChanged" }),
        [AuditActions.CustomerVehicleAdd] = new(AuditOutcome.Success, "CustomerVehicle", new[] { "customerId", "licensePlate", "vehicleTypeId" }),
        [AuditActions.CustomerVehicleRemove] = new(AuditOutcome.Success, "CustomerVehicle", new[] { "customerId", "licensePlate" }),
        [AuditActions.TicketCreate] = new(AuditOutcome.Success, "MonthlyTicket", new[] { "ticketCode", "customerId", "licensePlate", "planId", "startDate", "endDate", "price", "purchaseId" }),
        [AuditActions.TicketRenew] = new(AuditOutcome.Success, "MonthlyTicket", new[] { "ticketCode", "planId", "price", "purchaseId", "before", "after" }),
        [AuditActions.TicketSuspend] = new(AuditOutcome.Success, "MonthlyTicket", new[] { "ticketCode", "reason" }),
        [AuditActions.TicketResume] = new(AuditOutcome.Success, "MonthlyTicket", new[] { "ticketCode" }),
        [AuditActions.BlacklistAdd] = new(AuditOutcome.Success, "BlacklistEntry", new[] { "licensePlate", "reason" }),
        [AuditActions.BlacklistRemove] = new(AuditOutcome.Success, "BlacklistEntry", new[] { "licensePlate", "removeReason" }),
        [AuditActions.GateBlacklistBlocked] = new(AuditOutcome.Denied, "BlacklistEntry", new[] { "licensePlate", "normalizedPlate", "reason", "hadValidTicket" }),
        [AuditActions.GateBlacklistExitWarning] = new(AuditOutcome.Success, "ParkingSession", new[] { "licensePlate", "reason", "blacklistEntryId" }),
        [AuditActions.ZoneUpdate] = new(AuditOutcome.Success, "ParkingZone", new[] { "zoneCode", "before", "after" }),
    };

    private const string IdentityCard = "079099887766";

    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public ResidentVisitorAuditCatalogTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    public static TheoryData<string> Actions()
    {
        var data = new TheoryData<string>();
        foreach (var action in ResidentVisitorAuditActions.All)
        {
            data.Add(action);
        }

        return data;
    }

    [Fact]
    public void Catalogue_covers_every_resident_visitor_action_and_they_are_in_AuditActions_All()
    {
        Assert.Equal(ResidentVisitorAuditActions.All.OrderBy(a => a, StringComparer.Ordinal), Catalogue.Keys.OrderBy(a => a, StringComparer.Ordinal));
        Assert.Equal(13, Catalogue.Count);
        Assert.Superset(ResidentVisitorAuditActions.All.ToHashSet(StringComparer.Ordinal), AuditActions.All.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Operation_produces_expected_audit_row(string action)
    {
        _db.RequireAvailable();
        var expectation = Catalogue[action];

        var (idBefore, secrets) = await ProduceAsync(action);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, action);
        var matching = rows.Where(r => r.Outcome == expectation.Outcome).ToList();
        Assert.True(matching.Count >= 1, $"no {action}/{expectation.Outcome} row was written (found {rows.Count} {action} rows)");
        var row = matching[^1];
        Assert.Equal(expectation.EntityType, row.EntityType);
        Assert.False(string.IsNullOrWhiteSpace(row.EntityId));
        Assert.NotNull(row.UserId);
        AuditDb.HasKeys(row, expectation.Keys);
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);

        foreach (var r in await AuditDb.RowsAfterAsync(_db.Factory, idBefore))
        {
            Assert.DoesNotContain(IdentityCard, r.Details, StringComparison.Ordinal);
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, r.Details, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Runs the producing operation as a Manager; returns the audit watermark taken just before it and values that must not appear.</summary>
    private async Task<(long IdBefore, string[] Secrets)> ProduceAsync(string action)
    {
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var customers = sp.GetRequiredService<ICustomerService>();
        var tickets = sp.GetRequiredService<IMonthlyTicketService>();
        var blacklist = sp.GetRequiredService<IBlacklistService>();
        var gate = sp.GetRequiredService<IGateControlService>();
        var moto = await _data.MotorbikeTypeIdAsync();
        var planId = await _data.PlanIdAsync("Gói Xe Máy 1 Tháng");
        Task<long> Mark() => AuditDb.MaxIdAsync(_db.Factory);

        async Task<int> NewCustomerAsync(string phone, params NewVehicle[] vehicles)
        {
            var created = await customers.CreateCustomerAsync(new CustomerUpsertRequest
            {
                FullName = "Catalogue " + phone,
                PhoneNumber = phone,
                Email = "catalogue@example.com",
                IdentityCard = IdentityCard,
                IsResident = true,
                ApartmentCode = "K-0101",
                Vehicles = vehicles
            });
            Assert.True(created.Success, $"{created.Error}: {created.Message}");
            return created.Value;
        }

        switch (action)
        {
            case AuditActions.CustomerCreate:
            {
                var phone = ResidentVisitorData.UniquePhone();
                var id = await Mark();
                await NewCustomerAsync(phone);
                return (id, new[] { phone, "catalogue@example.com" });
            }

            case AuditActions.CustomerUpdate:
            {
                var phone = ResidentVisitorData.UniquePhone();
                var customerId = await NewCustomerAsync(phone);
                var id = await Mark();
                var result = await customers.UpdateCustomerAsync(customerId, new CustomerUpsertRequest
                {
                    FullName = "Catalogue updated",
                    PhoneNumber = phone,
                    Email = "changed@example.com",
                    IdentityCard = IdentityCard,
                    IsResident = true,
                    ApartmentCode = "K-0102"
                });
                Assert.True(result.Success, result.Message);
                return (id, new[] { phone, "changed@example.com" });
            }

            case AuditActions.CustomerVehicleAdd:
            {
                var customerId = await NewCustomerAsync(ResidentVisitorData.UniquePhone());
                var id = await Mark();
                Assert.True((await customers.AddVehicleAsync(customerId, new NewVehicle(ResidentVisitorData.UniqueNormalizedPlate(), moto))).Success);
                return (id, Array.Empty<string>());
            }

            case AuditActions.CustomerVehicleRemove:
            {
                var customerId = await NewCustomerAsync(ResidentVisitorData.UniquePhone());
                var added = await customers.AddVehicleAsync(customerId, new NewVehicle(ResidentVisitorData.UniqueNormalizedPlate(), moto));
                Assert.True(added.Success, added.Message);
                var id = await Mark();
                Assert.True((await customers.RemoveVehicleAsync(added.Value)).Success);
                return (id, Array.Empty<string>());
            }

            case AuditActions.TicketCreate:
            {
                var plate = ResidentVisitorData.UniqueNormalizedPlate();
                var customerId = await NewCustomerAsync(ResidentVisitorData.UniquePhone(), new NewVehicle(plate, moto));
                var id = await Mark();
                Assert.True((await tickets.CreateTicketAsync(new CreateTicketRequest(customerId, plate, planId))).Success);
                return (id, Array.Empty<string>());
            }

            case AuditActions.TicketRenew:
            case AuditActions.TicketSuspend:
            case AuditActions.TicketResume:
            {
                var plate = ResidentVisitorData.UniqueNormalizedPlate();
                var customerId = await NewCustomerAsync(ResidentVisitorData.UniquePhone(), new NewVehicle(plate, moto));
                var ticket = await tickets.CreateTicketAsync(new CreateTicketRequest(customerId, plate, planId));
                Assert.True(ticket.Success, ticket.Message);
                if (action == AuditActions.TicketResume)
                {
                    Assert.True((await tickets.SuspendTicketAsync(ticket.Value, "tạm")).Success);
                }

                var id = await Mark();
                var result = action switch
                {
                    AuditActions.TicketRenew => await tickets.RenewTicketAsync(ticket.Value, planId),
                    AuditActions.TicketSuspend => await tickets.SuspendTicketAsync(ticket.Value, "Khách yêu cầu"),
                    _ => await tickets.ResumeTicketAsync(ticket.Value)
                };
                Assert.True(result.Success, $"{result.Error}: {result.Message}");
                return (id, Array.Empty<string>());
            }

            case AuditActions.BlacklistAdd:
            {
                var id = await Mark();
                Assert.True((await blacklist.AddAsync(ResidentVisitorData.UniqueNormalizedPlate(), "Catalogue")).Success);
                return (id, Array.Empty<string>());
            }

            case AuditActions.BlacklistRemove:
            {
                var added = await blacklist.AddAsync(ResidentVisitorData.UniqueNormalizedPlate(), "Catalogue");
                Assert.True(added.Success, added.Message);
                var id = await Mark();
                Assert.True((await blacklist.RemoveAsync(added.Value, "Gỡ")).Success);
                return (id, Array.Empty<string>());
            }

            case AuditActions.GateBlacklistBlocked:
            {
                var plate = ResidentVisitorData.UniqueNormalizedPlate();
                await _data.AddBlacklistAsync(plate, "Catalogue block");
                var id = await Mark();
                var result = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = moto });
                Assert.True(result.IsBlacklisted);
                return (id, Array.Empty<string>());
            }

            case AuditActions.GateBlacklistExitWarning:
            {
                if (await sp.GetRequiredService<IShiftService>().GetActiveShiftAsync(manager.UserId) is null)
                {
                    await ParkingFlows.OpenShiftAsync(sp, manager.UserId);
                }

                var vt = await _data.CreateIsolatedVehicleTypeAsync();
                await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
                var plate = ResidentVisitorData.UniqueNormalizedPlate();
                var checkIn = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });
                Assert.True(checkIn.Success, checkIn.Message);
                await _data.AddBlacklistAsync(plate, "Catalogue exit");
                var id = await Mark();
                var (_, result) = await ParkingFlows.CheckOutCashAsync(sp, plate, manager.UserId);
                Assert.True(result.Success, result.Message);
                return (id, Array.Empty<string>());
            }

            case AuditActions.ZoneUpdate:
            {
                var vt = await _data.CreateIsolatedVehicleTypeAsync();
                var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
                var id = await Mark();
                Assert.True((await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zone.ZoneId, ZoneAudience.VisitorOnly)).Success);
                return (id, Array.Empty<string>());
            }

            default:
                throw new InvalidOperationException($"No producer for {action}");
        }
    }
}
