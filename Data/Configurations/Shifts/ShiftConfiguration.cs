using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Shifts;

namespace SmartPS.Data.Configurations.Shifts;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("Shifts");

        builder.HasKey(x => x.ShiftId);

        builder.Property(x => x.BeginningCash)
               .HasPrecision(18, 2);

        builder.Property(x => x.ExpectedCash)
               .HasPrecision(18, 2);

        builder.Property(x => x.ActualCash)
               .HasPrecision(18, 2);

        builder.Property(x => x.Difference)
               .HasPrecision(18, 2);

        builder.Property(x => x.Status)
               .HasDefaultValue(ShiftStatus.Active);

        builder.Property(x => x.ManagerNote)
               .HasMaxLength(1000);

        builder.HasIndex(x => new { x.OpenedByUserId, x.Status });
        builder.HasIndex(x => x.OpenedByUserId)
               .IsUnique()
               .HasDatabaseName("IX_Shifts_OneActivePerUser")
               .HasFilter("\"Status\" = 0");
        builder.HasIndex(x => x.OpenedAt);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.OpenedByUser)
               .WithMany()
               .HasForeignKey(x => x.OpenedByUserId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedByUser)
               .WithMany()
               .HasForeignKey(x => x.ReviewedByUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
