using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

public class ParkingZoneConfiguration : IEntityTypeConfiguration<ParkingZone>
{
    public void Configure(EntityTypeBuilder<ParkingZone> builder)
    {
        builder.ToTable("ParkingZones");

        builder.HasKey(x => x.ZoneId);

        builder.Property(x => x.ZoneCode)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(x => x.ZoneName)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(x => x.Description)
               .HasMaxLength(255);

        builder.Property(x => x.TotalCapacity)
               .HasDefaultValue(0);

        builder.HasIndex(x => x.ZoneCode)
               .IsUnique();

        builder.HasOne(x => x.VehicleType)
               .WithMany()
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Slots)
               .WithOne(x => x.Zone)
               .HasForeignKey(x => x.ZoneId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

