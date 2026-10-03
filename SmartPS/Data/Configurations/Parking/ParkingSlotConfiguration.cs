using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

public class ParkingSlotConfiguration : IEntityTypeConfiguration<ParkingSlot>
{
    public void Configure(EntityTypeBuilder<ParkingSlot> builder)
    {
        builder.ToTable("ParkingSlots");

        builder.HasKey(x => x.SlotId);

        builder.Property(x => x.SlotCode)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(x => x.ZoneName)
               .HasMaxLength(100);

        builder.Property(x => x.Status)
               .HasDefaultValue(SlotStatus.Available);

        builder.Property(x => x.CoordX)
               .HasDefaultValue(0.0);

        builder.Property(x => x.CoordY)
               .HasDefaultValue(0.0);

        builder.Property(x => x.Width)
               .HasDefaultValue(80.0);

        builder.Property(x => x.Height)
               .HasDefaultValue(120.0);

        builder.Property(x => x.CurrentLicensePlate)
               .HasMaxLength(30);

        builder.HasIndex(x => x.SlotCode)
               .IsUnique();

        builder.HasOne(x => x.Zone)
               .WithMany(z => z.Slots)
               .HasForeignKey(x => x.ZoneId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.VehicleType)
               .WithMany(v => v.ParkingSlots)
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

