using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

public class PricingPolicyVersionConfiguration : IEntityTypeConfiguration<PricingPolicyVersion>
{
    public void Configure(EntityTypeBuilder<PricingPolicyVersion> builder)
    {
        builder.ToTable("PricingPolicyVersions");

        builder.HasKey(x => x.VersionId);

        builder.Property(x => x.Block4hPrice).HasPrecision(18, 2);
        builder.Property(x => x.DailyPrice).HasPrecision(18, 2);
        builder.Property(x => x.Monthly1Price).HasPrecision(18, 2);
        builder.Property(x => x.Monthly3Price).HasPrecision(18, 2);
        builder.Property(x => x.Monthly6Price).HasPrecision(18, 2);
        builder.Property(x => x.OvernightSurcharge).HasPrecision(18, 2);
        builder.Property(x => x.OverQuotaDailyPrice).HasPrecision(18, 2);

        builder.HasIndex(x => new { x.VehicleTypeId, x.VersionNumber })
               .IsUnique();

        builder.HasOne(x => x.VehicleType)
               .WithMany()
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
