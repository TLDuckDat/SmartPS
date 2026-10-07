using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class CustomerVehicleConfiguration : IEntityTypeConfiguration<CustomerVehicle>
{
    public void Configure(EntityTypeBuilder<CustomerVehicle> builder)
    {
        builder.ToTable("CustomerVehicles");

        builder.HasKey(x => x.CustomerVehicleId);

        builder.Property(x => x.LicensePlate)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(x => x.IsActive)
               .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
               .IsRequired();

        // Giữ chỉ mục FK thường vì đã có chỉ mục lọc trên cột khác.
        builder.HasIndex(x => x.CustomerId, "IX_CustomerVehicles_CustomerId");

        builder.HasIndex(x => x.LicensePlate, "IX_CustomerVehicles_LicensePlate_Active")
               .IsUnique()
               .HasFilter("\"IsActive\"");

        builder.HasOne(x => x.VehicleType)
               .WithMany()
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
