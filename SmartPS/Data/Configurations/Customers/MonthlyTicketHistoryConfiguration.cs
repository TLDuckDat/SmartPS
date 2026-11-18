using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class MonthlyTicketHistoryConfiguration : IEntityTypeConfiguration<MonthlyTicketHistory>
{
    public void Configure(EntityTypeBuilder<MonthlyTicketHistory> builder)
    {
        builder.ToTable("MonthlyTicketHistories");

        builder.HasKey(x => x.HistoryId);

        builder.Property(x => x.OldLicensePlate)
               .HasMaxLength(20);

        builder.Property(x => x.NewLicensePlate)
               .HasMaxLength(20);

        builder.Property(x => x.Reason)
               .HasMaxLength(500);

        builder.Property(x => x.Amount)
               .HasPrecision(18, 2);

        // Khóa ngoại liên kết tới MonthlyTicket, chặn xóa cứng vé để bảo vệ lịch sử
        builder.HasOne(x => x.Ticket)
               .WithMany(t => t.Histories)
               .HasForeignKey(x => x.TicketId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TicketId);
        builder.HasIndex(x => x.CreatedAt);
    }
}
