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

        builder.Property(x => x.DefaultLicensePlate)
               .HasMaxLength(20);

        builder.Property(x => x.Type)
               .HasDefaultValue(CustomerType.Regular);

        builder.Property(x => x.CreatedAt)
               .IsRequired();

        builder.Property(x => x.IsActive)
               .HasDefaultValue(true);

        builder.Property(x => x.Notes)
               .HasMaxLength(500);

        builder.HasIndex(x => x.PhoneNumber);

        builder.HasOne(x => x.VehicleType)
               .WithMany()
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

