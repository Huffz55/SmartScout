using Microsoft.EntityFrameworkCore;
using SmartScout.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.Interfaces;

public interface ISmartScoutDbContext
{
    DbSet<Player> Players { get; }
    DbSet<PlayerSeasonStat> SeasonStats { get; }
    DbSet<BasketballLeague> Leagues { get; }
    DbSet<BasketballSeason> Seasons { get; }
    DbSet<IngestionJobLog> IngestionJobLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
