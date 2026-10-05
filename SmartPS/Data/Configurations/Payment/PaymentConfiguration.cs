using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Payment;

namespace SmartPS.Data.Configurations.Payment;

/// <summary> Cấu hình chung cho các bảng thanh toán. </summary>
public static class PaymentConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ConfigurePayment(modelBuilder.Entity<Models.Payment.Payment>());
        ConfigureTransaction(modelBuilder.Entity<PaymentTransaction>());
        ConfigureAttempt(modelBuilder.Entity<PaymentAttempt>());
        ConfigureWebhook(modelBuilder.Entity<PaymentWebhook>());
    }

    private static void ConfigurePayment(EntityTypeBuilder<Models.Payment.Payment> b)
    {
        b.ToTable("Payments");
        b.HasKey(x => x.PaymentId);
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(8).IsRequired();
        b.Property(x => x.Description).HasMaxLength(255).IsRequired();
        b.Property(x => x.CheckoutImagePath).HasMaxLength(500);
        b.HasIndex(x => x.SessionId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.SessionId)
            .HasFilter("\"Status\" IN (0, 1)")
            .IsUnique()
            .HasDatabaseName("IX_Payments_SessionId_Active");
        b.HasOne(x => x.Session).WithMany(s => s.Payments)
            .HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTransaction(EntityTypeBuilder<PaymentTransaction> b)
    {
        b.ToTable("PaymentTransactions");
        b.HasKey(x => x.PaymentTransactionId);
        b.Property(x => x.TransactionReference).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TransactionReference).IsUnique();
        b.Property(x => x.GatewayTransactionId).HasMaxLength(128);
        b.Property(x => x.GatewayOrderCode).HasMaxLength(64);
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(8).IsRequired();
        b.Property(x => x.QrCodePayload).HasMaxLength(4000);
        b.Property(x => x.CheckoutUrl).HasMaxLength(1000);
        b.Property(x => x.AccountNumber).HasMaxLength(64);
        b.Property(x => x.AccountName).HasMaxLength(128);
        b.Property(x => x.BankCode).HasMaxLength(32);
        b.HasIndex(x => x.GatewayOrderCode);
        b.HasIndex(x => x.PaymentId);
        b.HasOne(x => x.Payment).WithMany(p => p.Transactions)
            .HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAttempt(EntityTypeBuilder<PaymentAttempt> b)
    {
        b.ToTable("PaymentAttempts");
        b.HasKey(x => x.PaymentAttemptId);
        b.Property(x => x.Gateway).HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestPayload).HasColumnType("text");
        b.Property(x => x.ResponsePayload).HasColumnType("text");
        b.Property(x => x.ErrorCode).HasMaxLength(64);
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);
        b.HasIndex(x => x.PaymentTransactionId);
        b.HasOne(x => x.PaymentTransaction).WithMany(t => t.Attempts)
            .HasForeignKey(x => x.PaymentTransactionId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWebhook(EntityTypeBuilder<PaymentWebhook> b)
    {
        b.ToTable("PaymentWebhooks");
        b.HasKey(x => x.PaymentWebhookId);
        b.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        b.Property(x => x.EventId).HasMaxLength(256).IsRequired();
        b.Property(x => x.TransactionReference).HasMaxLength(64);
        b.Property(x => x.Payload).HasColumnType("text").IsRequired();
        b.Property(x => x.Signature).HasMaxLength(256);
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);
        b.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        b.HasIndex(x => x.TransactionReference);
        b.HasIndex(x => x.ReceivedAt);
        b.HasOne(x => x.PaymentTransaction).WithMany(t => t.Webhooks)
            .HasForeignKey(x => x.PaymentTransactionId).OnDelete(DeleteBehavior.SetNull);
    }
}
