using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

public class ParkingSettingsConfiguration : IEntityTypeConfiguration<ParkingSettings>
{
    public void Configure(EntityTypeBuilder<ParkingSettings> builder)
    {
        builder.ToTable("ParkingSettings");

        builder.HasKey(x => x.SettingsId);

        builder.Property(x => x.DefaultMaxVehiclesPerHousehold)
               .IsRequired()
               .HasDefaultValue(2);
    }
}
