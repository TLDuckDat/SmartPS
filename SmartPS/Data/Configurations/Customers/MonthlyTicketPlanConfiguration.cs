using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Parking;

namespace SmartPS.Data.Configurations.Customers;

public class MonthlyTicketPlanConfiguration : IEntityTypeConfiguration<MonthlyTicketPlan>
{
    public void Configure(EntityTypeBuilder<MonthlyTicketPlan> builder)
    {
        builder.ToTable("MonthlyTicketPlans");

        builder.HasKey(x => x.PlanId);

        builder.Property(x => x.PlanName)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(x => x.DurationMonths)
               .IsRequired();

        builder.Property(x => x.PricePerMonth)
               .HasPrecision(18, 2);

        builder.Property(x => x.DiscountPercentage)
               .HasDefaultValue(0.0);

        builder.Property(x => x.TotalPrice)
               .HasPrecision(18, 2);

        builder.Property(x => x.IsActive)
               .HasDefaultValue(true);

        builder.Property(x => x.Description)
               .HasMaxLength(255);

        builder.Ignore(x => x.DisplayText);

        builder.HasOne(x => x.VehicleType)
               .WithMany()
               .HasForeignKey(x => x.VehicleTypeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

