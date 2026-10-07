using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Audit;

namespace SmartPS.Data.Configurations.Audit;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(x => x.AuditLogId);

        builder.Property(x => x.OccurredAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(64);
        builder.Property(x => x.EntityId).HasMaxLength(128);
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Details).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(x => x.MachineName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PrevHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Hash).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => x.OccurredAtUtc).HasDatabaseName("IX_AuditLogs_OccurredAtUtc");
        builder.HasIndex(x => new { x.Action, x.OccurredAtUtc }).HasDatabaseName("IX_AuditLogs_Action_OccurredAtUtc");
        builder.HasIndex(x => x.UserId).HasDatabaseName("IX_AuditLogs_UserId");
        builder.HasIndex(x => x.Username).HasDatabaseName("IX_AuditLogs_Username");
        builder.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("IX_AuditLogs_EntityType_EntityId");
        builder.HasIndex(x => x.PrevHash).IsUnique().HasDatabaseName("IX_AuditLogs_PrevHash");
    }
}
