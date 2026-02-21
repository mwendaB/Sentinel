using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var actionConverter = new ValueConverter<RemediationActionDefinition?, string?>(
                action => action == null ? null : JsonSerializer.Serialize(action, jsonOptions),
                json => string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<RemediationActionDefinition>(json, jsonOptions));
            var actionComparer = new ValueComparer<RemediationActionDefinition?>(
                (left, right) => JsonSerializer.Serialize(left, jsonOptions) == JsonSerializer.Serialize(right, jsonOptions),
                value => value == null ? 0 : JsonSerializer.Serialize(value, jsonOptions).GetHashCode(),
                value => value == null
                    ? null
                    : JsonSerializer.Deserialize<RemediationActionDefinition>(JsonSerializer.Serialize(value, jsonOptions), jsonOptions));

            entity.Property(rule => rule.Action)
                .HasConversion(actionConverter)
                .HasColumnName("ActionJson")
                .Metadata.SetValueComparer(actionComparer);

            entity.Ignore(rule => rule.SourceTypes);
        });
    }
}
