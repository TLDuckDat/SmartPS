using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

/// <summary>Cấu hình bổ sung của báo cáo cho phiên gửi xe: chỉ mục theo giờ ra để lọc theo kỳ.</summary>
public class ParkingSessionReportingConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public void Configure(EntityTypeBuilder<ParkingSession> builder)
    {
        builder.HasIndex(x => x.CheckOutTime);
    }
}
