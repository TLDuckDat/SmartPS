using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;

namespace SmartPS.Tests.TestSupport;

/// <summary>Read-only helpers over the AuditLogs table for integration assertions.</summary>
public static class AuditDb
{
    public static async Task<long> MaxIdAsync(IDbContextFactory<SmartPsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.AuditLogs.AsNoTracking().Select(a => (long?)a.AuditLogId).MaxAsync() ?? 0L;
    }

    public static async Task<List<AuditLog>> RowsAfterAsync(IDbContextFactory<SmartPsDbContext> factory, long afterId, string? action = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.AuditLogs.AsNoTracking().Where(a => a.AuditLogId > afterId);
        if (action is not null)
        {
            query = query.Where(a => a.Action == action);
        }

        return await query.OrderBy(a => a.AuditLogId).ToListAsync();
    }

    public static async Task<List<AuditLog>> AllAsync(IDbContextFactory<SmartPsDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.AuditLogs.AsNoTracking().OrderBy(a => a.AuditLogId).ToListAsync();
    }

    public static JsonElement Details(AuditLog row)
    {
        using var doc = JsonDocument.Parse(row.Details);
        return doc.RootElement.Clone();
    }

    public static string[] StringArray(JsonElement element, string property)
    {
        Assert.True(element.TryGetProperty(property, out var array), $"Details has no '{property}' property: {element.GetRawText()}");
        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        return array.EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    public static string? String(JsonElement element, string property)
    {
        Assert.True(element.TryGetProperty(property, out var value), $"Details has no '{property}' property: {element.GetRawText()}");
        return value.ValueKind == JsonValueKind.Null ? null : value.ToString();
    }

    /// <summary>Asserts that every key exists at the top level of the details object.</summary>
    public static void HasKeys(AuditLog row, params string[] keys)
    {
        var details = Details(row);
        Assert.Equal(JsonValueKind.Object, details.ValueKind);
        foreach (var key in keys)
        {
            Assert.True(details.TryGetProperty(key, out _), $"{row.Action} details lack '{key}': {row.Details}");
        }
    }
}
