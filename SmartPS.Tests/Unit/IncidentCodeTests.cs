using SmartPS.Services.Incidents;

namespace SmartPS.Tests.Unit;

/// <summary>Incident codes are SC-&lt;Vietnam year&gt;-&lt;3-digit sequence&gt; and restart every year.</summary>
public class IncidentCodeTests
{
    [Fact]
    public void First_code_of_the_year_is_001()
    {
        Assert.Equal("SC-2026-001", IncidentService.NextIncidentCode(2026, Array.Empty<string>()));
    }

    [Fact]
    public void Next_code_follows_the_highest_number_of_the_same_year()
    {
        var existing = new[] { "SC-2026-001", "SC-2026-007", "SC-2026-003", "SC-2025-099" };

        Assert.Equal("SC-2026-008", IncidentService.NextIncidentCode(2026, existing));
    }

    [Fact]
    public void Sequence_grows_past_three_digits()
    {
        Assert.Equal("SC-2026-1000", IncidentService.NextIncidentCode(2026, new[] { "SC-2026-999" }));
    }

    [Fact]
    public void Malformed_codes_are_ignored()
    {
        Assert.Equal("SC-2026-002", IncidentService.NextIncidentCode(2026, new[] { "SC-2026-001", "SC-2026-ABC", "XX-2026-500" }));
    }
}
