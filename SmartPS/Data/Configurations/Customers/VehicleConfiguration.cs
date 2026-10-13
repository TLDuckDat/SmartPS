using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");

        builder.HasKey(x => x.VehicleId);

        builder.Property(x => x.LicensePlate)
               .HasMaxLength(20)
               .IsRequired();

        builder.HasIndex(x => x.LicensePlate)
               .IsUnique()
               .HasFilter("\"IsActive\" = true");

        builder.Property(x => x.Color)
               .HasMaxLength(50);

        builder.Property(x => x.Brand)
               .HasMaxLength(50);

        builder.HasOne(x => x.OwnerCustomer)
               .WithMany()
               .HasForeignKey(x => x.OwnerCustomerId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.VehicleType)
               .WithMany()
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
