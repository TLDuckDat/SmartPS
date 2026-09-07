using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class CustomerTierConfiguration : IEntityTypeConfiguration<CustomerTier>
{
    public void Configure(EntityTypeBuilder<CustomerTier> builder)
    {
        builder.ToTable("CustomerTiers");

        builder.HasKey(x => x.TierId);

        builder.Property(x => x.CustomerType)
               .IsRequired();

        builder.Property(x => x.TierName)
               .HasMaxLength(50)
               .IsRequired();

        builder.Property(x => x.DiscountPercentage)
               .HasDefaultValue(0.0);

        builder.Property(x => x.BadgeColor)
               .HasMaxLength(20)
               .HasDefaultValue("#64748B");

        builder.Property(x => x.BadgeIcon)
               .HasMaxLength(20)
               .HasDefaultValue("👤");

        builder.Property(x => x.Description)
               .HasMaxLength(255);

        builder.Property(x => x.IsActive)
               .HasDefaultValue(true);

        builder.Ignore(x => x.DisplayName);

        builder.HasIndex(x => x.CustomerType)
               .IsUnique();
    }
}

