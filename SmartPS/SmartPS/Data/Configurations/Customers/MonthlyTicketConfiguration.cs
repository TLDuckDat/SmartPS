using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class MonthlyTicketConfiguration : IEntityTypeConfiguration<MonthlyTicket>
{
    public void Configure(EntityTypeBuilder<MonthlyTicket> builder)
    {
        builder.ToTable("MonthlyTickets");

        builder.HasKey(x => x.TicketId);

        builder.Property(x => x.TicketCode)
               .HasMaxLength(50)
               .IsRequired();

        builder.Property(x => x.RegisteredLicensePlate)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(x => x.MonthlyPrice)
               .HasPrecision(18, 2);

        builder.Property(x => x.Status)
               .HasDefaultValue(MonthlyTicketStatus.Active);

        builder.Property(x => x.Notes)
               .HasMaxLength(500);

        builder.Ignore(x => x.IsCurrentlyValid);

        builder.HasIndex(x => x.TicketCode)
               .IsUnique();

        builder.HasIndex(x => x.RegisteredLicensePlate);

        builder.HasOne(x => x.Customer)
               .WithMany(c => c.MonthlyTickets)
               .HasForeignKey(x => x.CustomerId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Plan)
               .WithMany()
               .HasForeignKey(x => x.PlanId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.VehicleType)
               .WithMany()
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

