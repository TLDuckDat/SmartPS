using Microsoft.EntityFrameworkCore;
using SmartPS.Models.Auth;
namespace SmartPS.Data
{
    public class SmartPsDbContext : DbContext
    {
        public SmartPsDbContext(DbContextOptions<SmartPsDbContext> options) : base(options) { }
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartPsDbContext).Assembly);
        }
    }
}
