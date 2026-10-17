namespace SmartPS.Tests.Unit;

/// <summary>AC-12 (unit part), E7: chain verification stops at the first broken row and reports its Id.</summary>
public class AuditChainVerifierTests
{
    private static List<AuditLog> BuildChain(int count)
    {
        var rows = new List<AuditLog>();
        var prev = AuditHashing.GenesisHash;
        var start = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= count; i++)
        {
            var entry = new AuditEntry(AuditActions.AuthLoginSuccess, AuditOutcome.Success, "User", i.ToString(), new { n = i });
            var row = AuditRecordFactory.Create(entry, new AuditActor(i, $"user{i}", "Operator"), start.AddSeconds(i), "PC", prev);
            row.AuditLogId = i * 10; // ids need not be contiguous (identity gaps after rollbacks)
            rows.Add(row);
            prev = row.Hash;
        }

        return rows;
    }

    private static async IAsyncEnumerable<AuditLog> AsAsync(IEnumerable<AuditLog> rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    [Fact]
    public void Empty_chain_is_valid()
    {
        var result = AuditChainVerifier.Verify(Array.Empty<AuditLog>());

        Assert.True(result.IsValid);
        Assert.Equal(0, result.CheckedCount);
        Assert.Null(result.FirstInvalidAuditLogId);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Valid_factory_sets_count_and_no_failure()
    {
        var result = AuditVerificationResult.Valid(7);

        Assert.True(result.IsValid);
        Assert.Equal(7, result.CheckedCount);
        Assert.Null(result.FirstInvalidAuditLogId);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Intact_chain_of_five_is_valid()
    {
        var result = AuditChainVerifier.Verify(BuildChain(5));

        Assert.True(result.IsValid);
        Assert.Equal(5, result.CheckedCount);
        Assert.Null(result.FirstInvalidAuditLogId);
    }

    [Fact]
    public void First_row_must_start_from_genesis()
    {
        var rows = BuildChain(3);
        rows[0].PrevHash = new string('1', 64);
        rows[0].Hash = AuditHashing.ComputeHash(rows[0].PrevHash, rows[0]);

        var result = AuditChainVerifier.Verify(rows);

        Assert.False(result.IsValid);
        Assert.Equal(10, result.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.PrevHashMismatch, result.FailureReason);
    }

    [Fact]
    public void Tampered_content_reports_HashMismatch_at_that_row()
    {
        // AC-12: Given N>=3 rows, When one row's Username is edited (DBA with triggers disabled), Then Verify names that Id.
        var rows = BuildChain(5);
        rows[2].Username = "tampered";

        var result = AuditChainVerifier.Verify(rows);

        Assert.False(result.IsValid);
        Assert.Equal(30, result.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.HashMismatch, result.FailureReason);
    }

    [Fact]
    public void Tampered_details_are_detected()
    {
        var rows = BuildChain(4);
        rows[1].Details = "{\"n\":999}";

        var result = AuditChainVerifier.Verify(rows);

        Assert.False(result.IsValid);
        Assert.Equal(20, result.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.HashMismatch, result.FailureReason);
    }

    [Fact]
    public void Rehashed_row_breaks_the_link_of_the_next_row()
    {
        // The attacker edits row id 20 and recomputes its own hash: that row is self-consistent, id 30 no longer links.
        var rows = BuildChain(4);
        rows[1].Username = "tampered";
        rows[1].Hash = AuditHashing.ComputeHash(rows[1].PrevHash, rows[1]);

        var result = AuditChainVerifier.Verify(rows);

        Assert.False(result.IsValid);
        Assert.Equal(30, result.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.PrevHashMismatch, result.FailureReason);
    }

    [Fact]
    public void Deleted_middle_row_is_detected_at_the_following_row()
    {
        var rows = BuildChain(5);
        rows.RemoveAt(2); // id 30 removed

        var result = AuditChainVerifier.Verify(rows);

        Assert.False(result.IsValid);
        Assert.Equal(40, result.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.PrevHashMismatch, result.FailureReason);
    }

    [Fact]
    public void Verification_stops_at_first_failure()
    {
        var rows = BuildChain(5);
        rows[1].Username = "x";
        rows[3].Username = "y";

        var result = AuditChainVerifier.Verify(rows);

        Assert.Equal(20, result.FirstInvalidAuditLogId);
    }

    [Fact]
    public void Known_limitation_tail_deletion_is_not_detectable()
    {
        // Plan §1.5 (m13): removing the last rows leaves a consistent chain. Documented limitation, not a defect.
        var rows = BuildChain(5);
        rows.RemoveRange(3, 2);

        Assert.True(AuditChainVerifier.Verify(rows).IsValid);
    }

    [Fact]
    public async Task VerifyAsync_matches_Verify_for_valid_and_tampered_chains()
    {
        var valid = BuildChain(6);
        var tampered = BuildChain(6);
        tampered[4].EntityId = "changed";

        var validResult = await AuditChainVerifier.VerifyAsync(AsAsync(valid), TestContext.Current.CancellationToken);
        var tamperedResult = await AuditChainVerifier.VerifyAsync(AsAsync(tampered), TestContext.Current.CancellationToken);

        Assert.Equal(AuditChainVerifier.Verify(valid), validResult);
        Assert.Equal(AuditChainVerifier.Verify(tampered), tamperedResult);
        Assert.True(validResult.IsValid);
        Assert.Equal(6, validResult.CheckedCount);
        Assert.Equal(50, tamperedResult.FirstInvalidAuditLogId);
        Assert.Equal(AuditChainFailureReason.HashMismatch, tamperedResult.FailureReason);
    }
}
