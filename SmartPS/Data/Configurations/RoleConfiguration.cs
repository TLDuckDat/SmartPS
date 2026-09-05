using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPS.Models.Auth;

namespace SmartPS.Data.Configurations
{
    public class RoleConfiguration : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.ToTable("Roles");

            builder.HasKey(x => x.RoleId);

            builder.Property(x => x.RoleName)
                   .HasMaxLength(50)
                   .IsRequired();

            builder.Property(x => x.Description)
                   .HasMaxLength(255);

            builder.HasIndex(x => x.RoleName)
                   .IsUnique();

        }
    }
}
