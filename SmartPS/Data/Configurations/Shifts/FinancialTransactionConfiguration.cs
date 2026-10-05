using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Shifts;

namespace SmartPS.Data.Configurations.Shifts;

public class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("FinancialTransactions");

        builder.HasKey(x => x.TransactionId);

        builder.Property(x => x.TransactionCode)
               .HasMaxLength(50)
               .IsRequired();

        builder.Property(x => x.Amount)
               .HasPrecision(18, 2);

        builder.Property(x => x.ReferenceCode)
               .HasMaxLength(100);

        builder.Property(x => x.Note)
               .HasMaxLength(1000);

        builder.HasIndex(x => x.TransactionCode)
               .IsUnique();
        builder.HasIndex(x => x.ShiftId);
        builder.HasIndex(x => x.ParkingSessionId);
        builder.HasIndex(x => new { x.ParkingSessionId, x.Type })
               .IsUnique()
               .HasDatabaseName("IX_FinancialTransactions_OneParkingFeePerSession")
               .HasFilter("\"ParkingSessionId\" IS NOT NULL AND \"Type\" = 0");
        builder.HasIndex(x => x.CreatedByUserId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.Type);
        builder.HasIndex(x => x.PaymentMethod);

        builder.HasOne(x => x.Shift)
               .WithMany(x => x.Transactions)
               .HasForeignKey(x => x.ShiftId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ParkingSession)
               .WithMany()
               .HasForeignKey(x => x.ParkingSessionId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
