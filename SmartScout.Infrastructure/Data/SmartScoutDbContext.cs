using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
    public DbSet<BasketballLeague> Leagues { get; set; }
    public DbSet<BasketballSeason> Seasons { get; set; }
    public DbSet<IngestionJobLog> IngestionJobLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL Trigram extension for fast full-text & fuzzy ILIKE searches
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<BasketballLeague>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();

            entity.Property(e => e.Code).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Country).IsRequired().HasMaxLength(80);
            entity.Property(e => e.Region).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DifficultyCoefficient).HasPrecision(4, 2);
            entity.Property(e => e.ExternalIdsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<BasketballSeason>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EndYear).IsUnique();
            entity.HasIndex(e => e.Label).IsUnique();

            entity.Property(e => e.Label).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<IngestionJobLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.LeagueCode, e.Season }).IsUnique();

            entity.Property(e => e.LeagueCode).IsRequired().HasMaxLength(30);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(30);
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.ExternalApiId).IsUnique();

            // High performance GIN Trigram indexes for sub-millisecond text & fuzzy cross-source searching
            entity.HasIndex(e => e.NormalizedName)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

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

            entity.Property(e => e.FullName)
                .IsRequired()
                .HasMaxLength(205)
                .HasDefaultValue(string.Empty);

            entity.Property(e => e.NormalizedName)
                .IsRequired()
                .HasMaxLength(205)
                .HasDefaultValue(string.Empty);

            entity.Property(e => e.Position)
                .HasMaxLength(20);

            entity.Property(e => e.PrimaryPosition)
                .HasMaxLength(20);

            entity.Property(e => e.SecondaryPosition)
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

            entity.Property(e => e.ExternalIdsJson)
                .HasColumnType("jsonb");
                
            entity.HasMany(e => e.SeasonStats)
                .WithOne(e => e.Player)
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<PlayerSeasonStat>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Stint-based composite unique constraint: Player + Season + League + TeamName + CompetitionPhase
            entity.HasIndex(e => new { e.PlayerId, e.Season, e.League, e.TeamName, e.CompetitionPhase })
                .IsUnique();

            entity.Property(e => e.League)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("NBA");

            entity.Property(e => e.TeamName)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("TOT");

            entity.Property(e => e.CompetitionPhase)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Regular Season");

            entity.Property(e => e.LeagueDifficultyCoefficient)
                .HasPrecision(4, 2)
                .HasDefaultValue(1.00m);

            entity.Property(e => e.MinutesPlayedTotal)
                .HasPrecision(6, 1);

            entity.Property(e => e.Minutes)
                .HasPrecision(5, 1);

            entity.Property(e => e.Pir)
                .HasPrecision(5, 1);

            entity.Property(e => e.FoulsDrawn)
                .HasPrecision(5, 1);

            entity.Property(e => e.PersonalFoulsCommitted)
                .HasPrecision(5, 1);

            entity.Property(e => e.PlusMinus)
                .HasPrecision(5, 1);

            entity.Property(e => e.PtsPer36)
                .HasPrecision(5, 1);

            entity.Property(e => e.RebPer36)
                .HasPrecision(5, 1);

            entity.Property(e => e.AstPer36)
                .HasPrecision(5, 1);

            entity.Property(e => e.StlPer36)
                .HasPrecision(5, 1);

            entity.Property(e => e.BlkPer36)
                .HasPrecision(5, 1);

            entity.Property(e => e.TovPer36)
                .HasPrecision(5, 1);

            entity.Property(e => e.PointsPer40)
                .HasPrecision(5, 1);

            entity.Property(e => e.AssistsPer40)
                .HasPrecision(5, 1);

            entity.Property(e => e.ReboundsPer40)
                .HasPrecision(5, 1);

            entity.Property(e => e.EffectiveFgPct)
                .HasPrecision(5, 3);

            entity.Property(e => e.AssistToTurnoverRatio)
                .HasPrecision(5, 2);

            entity.Property(e => e.GameScoreAverage)
                .HasPrecision(5, 1);

            entity.Property(e => e.RawScrapedPayload)
                .HasColumnType("jsonb");
        });

        // Global DateTime UTC ValueConverter: guarantees all DateTime/DateTime? are written as UTC to PostgreSQL
        var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
            v => !v.HasValue ? null : (v.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v.Value.ToUniversalTime()),
            v => !v.HasValue ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(dateTimeConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableDateTimeConverter);
                }
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeDateTimesToUtc();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        NormalizeDateTimesToUtc();
        return base.SaveChanges();
    }

    private void NormalizeDateTimesToUtc()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            foreach (var prop in entry.Properties)
            {
                if (prop.CurrentValue is DateTime dt && dt.Kind == DateTimeKind.Unspecified)
                {
                    prop.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
            }
        }
    }
}
