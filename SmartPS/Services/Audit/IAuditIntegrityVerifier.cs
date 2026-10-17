namespace SmartPS.Services.Audit;

public interface IAuditIntegrityVerifier
{
    /// <summary>
    /// Walks the whole hash chain and reports the first inconsistent record. Requires Audit.Verify;
    /// the verification itself is recorded as AUDIT_VERIFY.
    /// </summary>
    Task<AuditVerificationResult> VerifyAsync(CancellationToken cancellationToken = default);
}
