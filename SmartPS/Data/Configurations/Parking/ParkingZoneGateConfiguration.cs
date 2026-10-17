using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

/// <summary>Cấu hình bổ sung của luồng cổng cho khu đỗ: đối tượng sử dụng mặc định là dùng chung.</summary>
public class ParkingZoneGateConfiguration : IEntityTypeConfiguration<ParkingZone>
{
    public void Configure(EntityTypeBuilder<ParkingZone> builder)
    {
        builder.Property(x => x.Audience)
               .HasDefaultValue(ZoneAudience.Mixed);
    }
}
