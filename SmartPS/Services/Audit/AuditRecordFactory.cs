using SmartPS.Models.Audit;

namespace SmartPS.Services.Audit;

public static class AuditRecordFactory
{
    public const int MaxUsernameLength = 100;
    public const int MaxRoleNameLength = 50;
    public const int MaxActionLength = 64;
    public const int MaxEntityTypeLength = 64;
    public const int MaxEntityIdLength = 128;
    public const int MaxMachineNameLength = 128;

    public static AuditLog Create(AuditEntry entry, AuditActor actor, DateTime occurredAtUtc, string machineName, string prevHash)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(actor);

        var row = new AuditLog
        {
            OccurredAtUtc = AuditHashing.NormalizeTimestamp(occurredAtUtc),
            UserId = actor.UserId,
            Username = Truncate(actor.Username, MaxUsernameLength) ?? string.Empty,
            RoleName = Truncate(actor.RoleName, MaxRoleNameLength) ?? string.Empty,
            Action = Truncate(entry.Action, MaxActionLength) ?? string.Empty,
            EntityType = Truncate(entry.EntityType, MaxEntityTypeLength),
            EntityId = Truncate(entry.EntityId, MaxEntityIdLength),
            Outcome = entry.Outcome,
            Details = AuditDetails.ToCanonicalJson(entry.Details),
            MachineName = Truncate(machineName, MaxMachineNameLength) ?? string.Empty,
            PrevHash = prevHash
        };

        row.Hash = AuditHashing.ComputeHash(prevHash, row);
        return row;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
