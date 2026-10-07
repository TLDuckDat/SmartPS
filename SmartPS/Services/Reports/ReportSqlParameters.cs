using Npgsql;
using NpgsqlTypes;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Models.Shifts;

namespace SmartPS.Services.Reports;

internal static class ReportSqlParameters
{
    /// <summary>Fresh typed parameters for one query (a parameter instance can only belong to one command).</summary>
    public static NpgsqlParameter[] Create(DateTime fromUtc, DateTime toUtc, ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return new[]
        {
            new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc) },
            new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc) },
            Integer("vt", filter.VehicleTypeId),
            Integer("zone", filter.ZoneId),
            Integer("grp", (int)filter.CustomerGroup),
            Integer("res", ReportCustomerGroupRules.ResidentCustomerTypeValue),
            Integer("active", (int)SessionStatus.Active),
            Integer("completed", (int)SessionStatus.Completed),
            Integer("cancelled", (int)SessionStatus.Cancelled),
            Integer("free", (int)PaymentMethod.Free),
            Integer("fee", (int)FinancialTransactionType.ParkingFee),
            Integer("refund", (int)FinancialTransactionType.Refund),
            Integer("adjust", (int)FinancialTransactionType.Adjustment),
            new NpgsqlParameter("sessionFiltered", NpgsqlDbType.Boolean) { Value = filter.HasSessionFilter },
            Integer("kindCreate", (int)TicketPurchaseKind.Create),
            Integer("kindRenew", (int)TicketPurchaseKind.Renew)
        };
    }

    private static NpgsqlParameter Integer(string name, int? value)
        => new(name, NpgsqlDbType.Integer) { Value = value.HasValue ? value.Value : DBNull.Value };
}
