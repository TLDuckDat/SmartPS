using SmartPS.Models.Parking;

namespace SmartPS.Tests.Unit;

/// <summary>
/// R1 / AC-4 / AC-R9 customer groups over sessions on the household model (Resident = 0, External = 1):
/// Resident ⇔ CustomerType.Resident (with or without a monthly ticket); MonthlyPass ⇔ IsMonthlyPass and CustomerType ≠ Resident;
/// Visitor ⇔ neither.
/// </summary>
public class ReportCustomerGroupRulesTests
{
    [Theory]
    [InlineData(CustomerType.Resident, true, ReportCustomerGroup.Resident)]
    [InlineData(CustomerType.Resident, false, ReportCustomerGroup.Resident)]
    [InlineData(CustomerType.External, true, ReportCustomerGroup.MonthlyPass)]
    [InlineData(CustomerType.External, false, ReportCustomerGroup.Visitor)]
    public void Classify(CustomerType type, bool isMonthlyPass, ReportCustomerGroup expected)
    {
        Assert.Equal(expected, ReportCustomerGroupRules.Classify(type, isMonthlyPass));
    }

    [Fact]
    public void Resident_value_is_the_household_model_enum_value()
    {
        Assert.Equal(0, ReportCustomerGroupRules.ResidentCustomerTypeValue);
        Assert.Equal((int)CustomerType.Resident, ReportCustomerGroupRules.ResidentCustomerTypeValue);
    }

    [Fact]
    public void CustomerType_has_only_Resident_0_and_External_1()
    {
        Assert.Equal(new[] { 0, 1 }, Enum.GetValues<CustomerType>().Select(v => (int)v).OrderBy(v => v));
        Assert.Equal(new[] { "External", "Resident" }, Enum.GetNames<CustomerType>().OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(1, (int)CustomerType.External);
    }

    [Fact]
    public void Sql_group_case_values_match_the_report_group_enum()
    {
        Assert.Equal("CASE WHEN s.\"CustomerType\" = @res THEN 1 WHEN s.\"IsMonthlyPass\" THEN 2 ELSE 3 END", ReportCustomerGroupRules.SqlGroupCase);
        Assert.Equal(1, (int)ReportCustomerGroup.Resident);
        Assert.Equal(2, (int)ReportCustomerGroup.MonthlyPass);
        Assert.Equal(3, (int)ReportCustomerGroup.Visitor);
    }

    [Fact]
    public void Sql_group_case_uses_the_resident_parameter_and_the_monthly_flag()
    {
        Assert.Contains("@res", ReportCustomerGroupRules.SqlGroupCase, StringComparison.Ordinal);
        Assert.Contains("\"IsMonthlyPass\"", ReportCustomerGroupRules.SqlGroupCase, StringComparison.Ordinal);
        Assert.Contains("\"CustomerType\"", ReportCustomerGroupRules.SqlGroupCase, StringComparison.Ordinal);
    }

    private static IQueryable<ParkingSession> Sessions()
    {
        var list = new List<ParkingSession>();
        var id = 1;
        void Add(CustomerType type, bool monthly, int count)
        {
            for (var i = 0; i < count; i++)
            {
                list.Add(new ParkingSession { SessionId = id++, CustomerType = type, IsMonthlyPass = monthly, LicensePlate = "P" + id, TicketCode = "T" + id });
            }
        }

        Add(CustomerType.Resident, true, 2);
        Add(CustomerType.Resident, false, 1);
        Add(CustomerType.External, true, 2);
        Add(CustomerType.External, false, 3);
        Add(CustomerType.External, false, 1);
        return list.AsQueryable();
    }

    [Theory]
    [InlineData(ReportCustomerGroup.All, 9)]
    [InlineData(ReportCustomerGroup.Resident, 3)]
    [InlineData(ReportCustomerGroup.MonthlyPass, 2)]
    [InlineData(ReportCustomerGroup.Visitor, 4)]
    public void Apply_filters_an_in_memory_queryable(ReportCustomerGroup group, int expected)
    {
        var filtered = ReportCustomerGroupRules.Apply(Sessions(), group).ToList();

        Assert.Equal(expected, filtered.Count);
        if (group != ReportCustomerGroup.All)
        {
            Assert.All(filtered, s => Assert.Equal(group, ReportCustomerGroupRules.Classify(s.CustomerType, s.IsMonthlyPass)));
        }
    }

    [Fact]
    public void Groups_partition_all_sessions()
    {
        var all = Sessions();
        var total = ReportCustomerGroupRules.Apply(all, ReportCustomerGroup.Resident).Count()
                    + ReportCustomerGroupRules.Apply(all, ReportCustomerGroup.MonthlyPass).Count()
                    + ReportCustomerGroupRules.Apply(all, ReportCustomerGroup.Visitor).Count();

        Assert.Equal(all.Count(), total);
    }
}
