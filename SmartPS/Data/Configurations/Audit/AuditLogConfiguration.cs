using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartPS.Models.Audit;

namespace SmartPS.Data.Configurations.Audit;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public const string AuditLogTriggerName = "TR_AuditLogs_BlockMutation";

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        // Trigger chặn UPDATE/DELETE (tạo trong migration). Khai báo trigger để EF không dùng mệnh đề OUTPUT khi INSERT,
        // vì SQL Server không cho dùng OUTPUT (không INTO) trên bảng có trigger.
        builder.ToTable("AuditLogs", t => t.HasTrigger(AuditLogTriggerName));

        builder.HasKey(x => x.AuditLogId);
        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(64);
        builder.Property(x => x.EntityId).HasMaxLength(128);
        // Unknown text in the column (for example after tampering) must not break reading: it maps to an invalid value
        // that never equals the stored text, so the hash chain verification reports that record.
        builder.Property(x => x.Outcome)
               .HasConversion(new ValueConverter<AuditOutcome, string>(v => v.ToString(), v => ParseOutcome(v)))
               .HasMaxLength(16)
               .IsRequired();
        // JSON chuẩn hoá (AuditDetails) lưu nguyên văn; ISJSON kiểm tra trong migration
        builder.Property(x => x.Details).HasColumnType("nvarchar(max)").HasDefaultValueSql("N'{}'").IsRequired();
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

    // Only the exact enum names are valid: numbers, flags, whitespace or different casing are treated as tampering.
    private static AuditOutcome ParseOutcome(string value) => value switch
    {
        "Success" => AuditOutcome.Success,
        "Denied" => AuditOutcome.Denied,
        "Failed" => AuditOutcome.Failed,
        _ => (AuditOutcome)(-1)
    };
}
