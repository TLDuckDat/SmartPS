using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

public class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> builder)
    {
        builder.ToTable("PricingRules");

        builder.HasKey(x => x.RuleId);

        builder.Property(x => x.Block4hPrice)
               .HasPrecision(18, 2);

        builder.Property(x => x.DailyPrice)
               .HasPrecision(18, 2);

        builder.Property(x => x.Monthly1Price)
               .HasPrecision(18, 2);

        builder.Property(x => x.Monthly3Price)
               .HasPrecision(18, 2);

        builder.Property(x => x.Monthly6Price)
               .HasPrecision(18, 2);

        builder.HasIndex(x => x.VehicleTypeId)
               .IsUnique();

        builder.Property(x => x.Description)
               .HasMaxLength(255);

        builder.HasOne(x => x.VehicleType)
               .WithMany(v => v.PricingRules)
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

