namespace SmartPS.Services.Audit;

public enum AuditChainFailureReason
{
    PrevHashMismatch,
    HashMismatch
}

public sealed record AuditVerificationResult(
    bool IsValid,
    long CheckedCount,
    long? FirstInvalidAuditLogId,
    AuditChainFailureReason? FailureReason)
{
    public static AuditVerificationResult Valid(long checkedCount) => new(true, checkedCount, null, null);
}
