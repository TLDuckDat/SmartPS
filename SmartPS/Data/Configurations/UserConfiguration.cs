using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Auth;


namespace SmartPS.Data.Configurations
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(x => x.UserId);

            builder.Property(x => x.Username)
                   .HasMaxLength(50)
                   .IsRequired();

            builder.Property(x => x.PasswordHash)
                   .HasMaxLength(255)
                   .IsRequired();

            builder.Property(x => x.FullName)
                   .HasMaxLength(100)
                   .IsRequired();

            builder.Property(x => x.IsActive)
                   .HasDefaultValue(true);

            builder.HasIndex(x => x.Username)
                   .IsUnique();

            builder.HasOne(x => x.Role)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

        }
    }
}
