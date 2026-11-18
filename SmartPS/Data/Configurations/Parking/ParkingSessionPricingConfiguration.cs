using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

/// <summary>Cấu hình bản chụp bảng giá và chi tiết cước lưu cùng phiên gửi xe (tách riêng khỏi ParkingSessionConfiguration).</summary>
public class ParkingSessionPricingConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public void Configure(EntityTypeBuilder<ParkingSession> builder)
    {
        builder.Property(x => x.FeeBreakdownJson);

        builder.HasOne(x => x.PricingPolicyVersion)
               .WithMany()
               .HasForeignKey(x => x.PricingPolicyVersionId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
