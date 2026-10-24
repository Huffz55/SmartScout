using Microsoft.EntityFrameworkCore;
using SmartScout.Application.Interfaces;
using SmartScout.Domain.Entities;

namespace SmartScout.Infrastructure.Data;

public class SmartScoutDbContext : DbContext, ISmartScoutDbContext
{
    public SmartScoutDbContext(DbContextOptions<SmartScoutDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players { get; set; }
    public DbSet<PlayerSeasonStat> SeasonStats { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL Trigram extension for fast full-text ILIKE searches
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.ExternalApiId).IsUnique();

            // High performance GIN Trigram indexes for sub-millisecond text searching
            entity.HasIndex(e => e.LastName)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.HasIndex(e => e.FirstName)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Position)
                .HasMaxLength(20);

            entity.Property(e => e.Height)
                .HasMaxLength(20);

            entity.Property(e => e.TeamName)
                .HasMaxLength(100);

            entity.Property(e => e.League)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Nationality)
                .HasMaxLength(50);
                
            entity.HasMany(e => e.SeasonStats)
                .WithOne(e => e.Player)
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<PlayerSeasonStat>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.PlayerId, e.Season }).IsUnique();
            
            entity.Property(e => e.Minutes)
                .HasPrecision(4, 1);

            entity.Property(e => e.Pir)
                .HasPrecision(4, 1);

            entity.Property(e => e.FoulsDrawn)
                .HasPrecision(4, 1);

            entity.Property(e => e.PointsPer40)
                .HasPrecision(4, 1);

            entity.Property(e => e.AssistsPer40)
                .HasPrecision(4, 1);

            entity.Property(e => e.ReboundsPer40)
                .HasPrecision(4, 1);
        });
    }
}
