using Microsoft.EntityFrameworkCore;
using Sentinel.Core.Models;

namespace Sentinel.Api.Persistence;

public sealed class SentinelDbContext : DbContext
{
    public SentinelDbContext(DbContextOptions<SentinelDbContext> options)
        : base(options)
    {
    }

    public DbSet<LogEvent> LogEvents => Set<LogEvent>();
    public DbSet<ActionEvent> ActionEvents => Set<ActionEvent>();
    public DbSet<RuleDefinition> Rules => Set<RuleDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogEvent>(entity =>
        {
            entity.HasKey(log => log.Id);
            entity.OwnsOne(log => log.Source, source =>
            {
                source.Property(s => s.Name).HasColumnName("SourceName");
                source.Property(s => s.Type).HasColumnName("SourceType");
                source.Property(s => s.Platform).HasColumnName("SourcePlatform");
            });
            entity.Ignore(log => log.Metadata);
        });

        modelBuilder.Entity<ActionEvent>(entity =>
        {
            entity.HasKey(action => action.Id);
        });

        modelBuilder.Entity<RuleDefinition>(entity =>
        {
            entity.HasKey(rule => rule.Id);
            entity.Ignore(rule => rule.SourceTypes);
        });
    }
}
