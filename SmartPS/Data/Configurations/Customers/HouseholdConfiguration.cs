using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("Households");

        builder.HasKey(x => x.HouseholdId);

        builder.Property(x => x.ApartmentCode)
               .HasMaxLength(20)
               .IsRequired();

        builder.HasIndex(x => x.ApartmentCode)
               .IsUnique();

        builder.Property(x => x.Notes)
               .HasMaxLength(500);
    }
}
