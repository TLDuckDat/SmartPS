using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

/// <summary>Cấu hình bổ sung của luồng cổng cho phiên gửi xe: mỗi ô chỉ có một phiên đang hoạt động.</summary>
public class ParkingSessionGateConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public void Configure(EntityTypeBuilder<ParkingSession> builder)
    {
        // Giữ chỉ mục FK thường và thêm chỉ mục duy nhất một phiên đang hoạt động trên mỗi ô.
        builder.HasIndex(x => x.SlotId, "IX_ParkingSessions_SlotId");
        builder.HasIndex(x => x.SlotId, "IX_ParkingSessions_SlotId_Active")
               .IsUnique()
               .HasFilter("\"Status\" = 0 AND \"SlotId\" IS NOT NULL");
    }
}
