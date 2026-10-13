using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(x => x.CustomerId);

        builder.Property(x => x.FullName)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(x => x.PhoneNumber)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(x => x.Email)
               .HasMaxLength(100);

        builder.Property(x => x.IdentityCard)
               .HasMaxLength(30);

        builder.Property(x => x.Type)
               .IsRequired();

        builder.Property(x => x.CreatedAt)
               .IsRequired();

        builder.Property(x => x.IsActive)
               .HasDefaultValue(true);

        builder.Property(x => x.Notes)
               .HasMaxLength(500);

        builder.HasIndex(x => x.PhoneNumber);

        builder.HasOne(x => x.Household)
               .WithMany(h => h.Customers)
               .HasForeignKey(x => x.HouseholdId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

