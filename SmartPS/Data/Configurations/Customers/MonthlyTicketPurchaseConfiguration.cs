using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class MonthlyTicketPurchaseConfiguration : IEntityTypeConfiguration<MonthlyTicketPurchase>
{
    public void Configure(EntityTypeBuilder<MonthlyTicketPurchase> builder)
    {
        builder.ToTable("MonthlyTicketPurchases");

        builder.HasKey(x => x.MonthlyTicketPurchaseId);

        builder.Property(x => x.Kind)
               .IsRequired();

        builder.Property(x => x.Price)
               .HasPrecision(18, 2);

        builder.Property(x => x.PeriodStartUtc)
               .IsRequired();

        builder.Property(x => x.PeriodEndUtc)
               .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
               .IsRequired();

        builder.HasIndex(x => x.CreatedAtUtc, "IX_MonthlyTicketPurchases_CreatedAtUtc");

        builder.HasOne(x => x.Ticket)
               .WithMany(t => t.Purchases)
               .HasForeignKey(x => x.TicketId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Plan)
               .WithMany()
               .HasForeignKey(x => x.PlanId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
