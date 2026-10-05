using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Parking;

public class ParkingSessionConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public void Configure(EntityTypeBuilder<ParkingSession> builder)
    {
        builder.ToTable("ParkingSessions");

        builder.HasKey(x => x.SessionId);

        builder.Property(x => x.TicketCode)
               .HasMaxLength(50)
               .IsRequired();

        builder.Property(x => x.LicensePlate)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(x => x.CheckInImagePath)
               .HasMaxLength(500);

        builder.Property(x => x.CheckOutImagePath)
               .HasMaxLength(500);

        builder.Property(x => x.TotalFee)
               .HasPrecision(18, 2)
               .HasDefaultValue(0.0m);

        builder.Property(x => x.Status)
               .HasDefaultValue(SessionStatus.Active);

        builder.Property(x => x.PaymentMethod)
               .HasDefaultValue(PaymentMethod.Cash);

        builder.Property(x => x.IsMonthlyPass)
               .HasDefaultValue(false);

        builder.Property(x => x.CustomerType)
               .HasDefaultValue(CustomerType.Regular);

        builder.HasIndex(x => x.TicketCode);
        builder.HasIndex(x => x.LicensePlate);
        builder.HasIndex(x => x.CheckInTime);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.VehicleType)
               .WithMany(v => v.ParkingSessions)
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Slot)
               .WithMany(s => s.ParkingSessions)
               .HasForeignKey(x => x.SlotId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Customer)
               .WithMany(c => c.ParkingSessions)
               .HasForeignKey(x => x.CustomerId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedByUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

