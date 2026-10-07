using SmartPS.Models.Audit;

namespace SmartPS.Services.Audit;

/// <summary>Filter for the audit log screen. Dates are Vietnam-local calendar dates (UTC+7).</summary>
public sealed record AuditQueryFilter
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public DateOnly? FromDateVn { get; init; }
    public DateOnly? ToDateVn { get; init; }
    public string? Username { get; init; }
    public string? Action { get; init; }
    public AuditOutcome? Outcome { get; init; }

    /// <summary>Matches the EntityId or any text inside Details (for example a license plate).</summary>
    public string? SearchText { get; init; }

    public int PageIndex { get; init; } = 0;

    /// <summary>Clamped to [1, 200] by the query service.</summary>
    public int PageSize { get; init; } = DefaultPageSize;
}
