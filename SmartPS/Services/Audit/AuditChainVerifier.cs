using SmartPS.Models.Audit;

namespace SmartPS.Services.Audit;

/// <summary>Walks the audit chain in Id order and reports the first inconsistent record.</summary>
public static class AuditChainVerifier
{
    public static AuditVerificationResult Verify(IEnumerable<AuditLog> entriesOrderedById)
    {
        ArgumentNullException.ThrowIfNull(entriesOrderedById);

        var expectedPrev = AuditHashing.GenesisHash;
        long checkedCount = 0;
        foreach (var entry in entriesOrderedById)
        {
            var failure = Check(entry, expectedPrev);
            if (failure is not null)
            {
                return new AuditVerificationResult(false, checkedCount, entry.AuditLogId, failure);
            }

            expectedPrev = entry.Hash;
            checkedCount++;
        }

        return AuditVerificationResult.Valid(checkedCount);
    }

    public static async Task<AuditVerificationResult> VerifyAsync(
        IAsyncEnumerable<AuditLog> entriesOrderedById,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entriesOrderedById);

        var expectedPrev = AuditHashing.GenesisHash;
        long checkedCount = 0;
        await foreach (var entry in entriesOrderedById.WithCancellation(cancellationToken))
        {
            var failure = Check(entry, expectedPrev);
            if (failure is not null)
            {
                return new AuditVerificationResult(false, checkedCount, entry.AuditLogId, failure);
            }

            expectedPrev = entry.Hash;
            checkedCount++;
        }

        return AuditVerificationResult.Valid(checkedCount);
    }

    private static AuditChainFailureReason? Check(AuditLog entry, string expectedPrev)
    {
        if (!string.Equals(entry.PrevHash, expectedPrev, StringComparison.Ordinal))
        {
            return AuditChainFailureReason.PrevHashMismatch;
        }

        if (!string.Equals(AuditHashing.ComputeHash(entry.PrevHash, entry), entry.Hash, StringComparison.Ordinal))
        {
            return AuditChainFailureReason.HashMismatch;
        }

        return null;
    }
}
