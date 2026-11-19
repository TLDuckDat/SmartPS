using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Services.Pricing;

/// <summary>
/// Phí tạm tính cho xe đang trong bãi, chỉ để hiển thị. Dùng cùng bộ tính cước và cùng ngữ cảnh giá
/// (bảng giá hiện hành, vé tháng, xe vượt định mức) như lúc xe ra; số tiền thu thật vẫn do
/// <see cref="IGateControlService"/> tính lại khi hoàn tất cho xe ra.
/// </summary>
public interface IParkingFeeEstimator
{
    /// <summary>
    /// Nạp ngữ cảnh giá cho các phiên đang gửi (truy vấn CSDL). Phiên không nạp được thì không có trong kết quả.
    /// </summary>
    Task<IReadOnlyDictionary<int, PricingContext>> ResolveContextsAsync(
        IEnumerable<ParkingSession> sessions, CancellationToken cancellationToken = default);

    /// <summary>Tính phí tạm tính tại <paramref name="nowUtc"/> từ ngữ cảnh đã nạp (không truy cập CSDL).</summary>
    decimal Estimate(ParkingSession session, PricingContext context, DateTime nowUtc);
}

public sealed class ParkingFeeEstimator : IParkingFeeEstimator
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbContextFactory;
    private readonly IParkingFeeCalculator _feeCalculator;

    public ParkingFeeEstimator(IDbContextFactory<SmartPsDbContext> dbContextFactory, IParkingFeeCalculator feeCalculator)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _feeCalculator = feeCalculator ?? throw new ArgumentNullException(nameof(feeCalculator));
    }

    public async Task<IReadOnlyDictionary<int, PricingContext>> ResolveContextsAsync(
        IEnumerable<ParkingSession> sessions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var active = sessions.Where(s => s.Status == SessionStatus.Active && s.SessionId > 0).ToList();
        var result = new Dictionary<int, PricingContext>();
        if (active.Count == 0)
        {
            return result;
        }

        var nowUtc = DateTime.UtcNow;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        foreach (var session in active)
        {
            try
            {
                result[session.SessionId] = await PricingContextResolver.ResolveAsync(db, session, nowUtc, null, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Một phiên lỗi không làm hỏng cả danh sách; phiên đó chỉ không có phí tạm tính
                System.Diagnostics.Debug.WriteLine($"[ParkingFeeEstimator] Session {session.SessionId}: {ex.GetType().Name}");
            }
        }

        return result;
    }

    public decimal Estimate(ParkingSession session, PricingContext context, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        return _feeCalculator
            .CalculateFee(session, context.Policy, nowUtc, context.MonthlyPass, context.IsOverQuota)
            .TotalFee;
    }
}
