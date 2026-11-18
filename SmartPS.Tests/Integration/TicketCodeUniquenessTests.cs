using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Mã vé vãng lai phải duy nhất kể cả sau khi ứng dụng khởi động lại (bộ đếm trong bộ nhớ bắt đầu lại từ 1001).
/// Lỗi đã gặp: hai xe khác biển cùng nhận mã TK-yyyyMMdd-1001 sau một lần mở lại ứng dụng.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class TicketCodeUniquenessTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public TicketCodeUniquenessTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    [Fact]
    public async Task Walk_in_ticket_codes_stay_unique_across_service_restarts()
    {
        var codes = new List<string>();

        // Mỗi vòng dựng một container mới: giống như mở lại ứng dụng (bộ đếm trong GateControlService bắt đầu lại)
        for (var run = 0; run < 2; run++)
        {
            var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
            using var sp = IntegrationServices.Create(_db);
            await sp.LoginAsync(op.Username);
            var gate = sp.GetRequiredService<IGateControlService>();

            var plate = $"77R{run}-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            var result = await gate.ProcessCheckInAsync(new GateCheckInRequest
            {
                LicensePlate = plate,
                VehicleTypeId = 1
            });

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Session);
            codes.Add(result.Session!.TicketCode);
        }

        Assert.NotEqual(codes[0], codes[1]);
    }
}
