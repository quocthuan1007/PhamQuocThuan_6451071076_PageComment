using Microsoft.EntityFrameworkCore;
using SharedDomain;

namespace CoreService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProcessState> ProcessStates { get; set; }
    public DbSet<UserBlacklist> UserBlacklists { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessState>().HasKey(e => e.Id);
        modelBuilder.Entity<ProcessState>().HasIndex(e => e.EventId).IsUnique();

        modelBuilder.Entity<UserBlacklist>().HasKey(e => e.Id);
        modelBuilder.Entity<UserBlacklist>().HasIndex(e => e.SenderId).IsUnique();
    }
}
