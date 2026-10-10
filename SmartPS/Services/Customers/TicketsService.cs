using SmartPS.Models.Parking;
using SmartPS.Data;
using Microsoft.EntityFrameworkCore;
using SmartPS.Services.GateControl;
using System.IO;

namespace SmartPS.Services.Customers;

public class TicketsService
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;
    private readonly IGateControlService _gateService;

    public TicketsService(IDbContextFactory<SmartPsDbContext> dbFactory, IGateControlService gateService)
    {
        _dbFactory = dbFactory;
        _gateService = gateService;
    }

    public async Task LogAuditAsync(string action, string details)
    {
        var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"{action}\",\"details\":\"{details}\"}}\n";
        await File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);
    }
}
