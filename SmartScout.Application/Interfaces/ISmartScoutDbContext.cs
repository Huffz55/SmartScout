using Microsoft.EntityFrameworkCore;
using SmartScout.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.Interfaces;

public interface ISmartScoutDbContext
{
    DbSet<Player> Players { get; }
    DbSet<PlayerSeasonStat> SeasonStats { get; }
    DbSet<PlayerInjury> PlayerInjuries { get; }
    DbSet<BasketballLeague> Leagues { get; }
    DbSet<BasketballSeason> Seasons { get; }
    DbSet<IngestionJobLog> IngestionJobLogs { get; }
    DbSet<ScoutBoard> ScoutBoards { get; }
    DbSet<ScoutBoardEntry> ScoutBoardEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
