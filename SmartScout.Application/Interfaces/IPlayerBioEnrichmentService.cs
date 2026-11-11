using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartScout.Application.Interfaces;

public record BioEnrichmentSummaryDto(
    int TotalProcessed,
    int TotalEnriched,
    int TotalFailed,
    List<EnrichedPlayerEntryDto> EnrichedPlayers);

public record EnrichedPlayerEntryDto(
    string FullName,
    string League,
    string? BirthDate,
    int? Age,
    int? HeightCm,
    int? WeightKg,
    string? Position,
    int? DraftYear);

public interface IPlayerBioEnrichmentService
{
    Task<BioEnrichmentSummaryDto> EnrichPlayerBiosAsync(
        string? league = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<BioEnrichmentSummaryDto> EnrichAllDatabasePlayersAsync(
        int concurrency = 5,
        CancellationToken cancellationToken = default);
}
