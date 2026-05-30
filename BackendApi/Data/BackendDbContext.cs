using BackendApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Data;

public class BackendDbContext : DbContext
{
    public BackendDbContext(DbContextOptions<BackendDbContext> options) : base(options)
    {
    }

    public DbSet<ProcessedCommand> ProcessedCommands => Set<ProcessedCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedCommand>()
            .HasIndex(x => x.CommandId)
            .IsUnique();
    }
}
