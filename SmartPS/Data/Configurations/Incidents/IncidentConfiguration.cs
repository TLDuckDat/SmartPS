using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Incidents;

namespace SmartPS.Data.Configurations.Incidents;

public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public const int CodeMaxLength = 20;
    public const int LicensePlateMaxLength = 20;
    public const int TextMaxLength = 1000;

    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents");

        builder.HasKey(x => x.IncidentId);

        builder.Property(x => x.IncidentCode)
               .IsRequired()
               .HasMaxLength(CodeMaxLength);

        builder.Property(x => x.LicensePlate)
               .HasMaxLength(LicensePlateMaxLength);

        builder.Property(x => x.Description)
               .IsRequired()
               .HasMaxLength(TextMaxLength);

        builder.Property(x => x.ResolutionNotes)
               .HasMaxLength(TextMaxLength);

        builder.Property(x => x.Status)
               .HasDefaultValue(IncidentStatus.Open);

        builder.HasIndex(x => x.IncidentCode).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ReportedAt);

        builder.HasOne(x => x.ReportedByUser)
               .WithMany()
               .HasForeignKey(x => x.ReportedByUserId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ResolvedByUser)
               .WithMany()
               .HasForeignKey(x => x.ResolvedByUserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
