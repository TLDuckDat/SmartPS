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

        builder.Property(x => x.FirstBlockMinutes)
               .HasDefaultValue(120);

        builder.Property(x => x.FirstBlockPrice)
               .HasPrecision(18, 2);

        builder.Property(x => x.AdditionalPricePerHour)
               .HasPrecision(18, 2);

        builder.Property(x => x.OvernightPrice)
               .HasPrecision(18, 2);

        builder.Property(x => x.Description)
               .HasMaxLength(255);

        builder.HasOne(x => x.VehicleType)
               .WithMany(v => v.PricingRules)
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

