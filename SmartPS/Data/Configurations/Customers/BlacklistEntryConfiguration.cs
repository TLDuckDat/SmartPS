using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class BlacklistEntryConfiguration : IEntityTypeConfiguration<BlacklistEntry>
{
    public void Configure(EntityTypeBuilder<BlacklistEntry> builder)
    {
        builder.ToTable("BlacklistEntries");

        builder.HasKey(x => x.BlacklistEntryId);

        builder.Property(x => x.LicensePlate)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(x => x.Reason)
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(x => x.RemoveReason)
               .HasMaxLength(500);

        builder.Property(x => x.IsActive)
               .HasDefaultValue(true);

        builder.HasIndex(x => x.LicensePlate, "IX_BlacklistEntries_LicensePlate");

        builder.HasIndex(x => x.LicensePlate, "IX_BlacklistEntries_LicensePlate_Active")
               .IsUnique()
               .HasFilter("\"IsActive\"");

        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedByUserId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RemovedByUser)
               .WithMany()
               .HasForeignKey(x => x.RemovedByUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
