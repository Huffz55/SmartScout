using SmartScout.Application.DTOs;
using SmartScout.Domain.Entities;
using System.Collections.Generic;

namespace SmartScout.Application.Interfaces;

public interface ISmartScoutSearchEngine
{
    List<SmartScoutSearchResultDto> Search(
        string prompt, 
        IEnumerable<PlayerSeasonStat> candidateStats, 
        int top = 20);
}
