using System;
using System.Collections.Generic;

namespace SmartScout.Domain.Entities;

public class Player
{
    public Guid Id { get; private set; }
    public int ExternalApiId { get; private set; } // e.g. from balldontlie
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Position { get; private set; }
    public string? Height { get; private set; }
    public int? WeightPounds { get; private set; }
    public string? TeamName { get; private set; }
    public string League { get; private set; } = string.Empty;

    private readonly List<PlayerSeasonStat> _seasonStats = new();
    public IReadOnlyCollection<PlayerSeasonStat> SeasonStats => _seasonStats.AsReadOnly();

    // EF Core constructor
    private Player() { }

    public Player(int externalApiId, string firstName, string lastName, string? position, string? height, int? weightPounds, string? teamName, string league)
    {
        Id = Guid.NewGuid();
        ExternalApiId = externalApiId;
        FirstName = firstName;
        LastName = lastName;
        Position = position;
        Height = height;
        WeightPounds = weightPounds;
        TeamName = teamName;
        League = league;
    }

    public void UpdateDetails(string firstName, string lastName, string? position, string? height, int? weightPounds, string? teamName, string league)
    {
        FirstName = firstName;
        LastName = lastName;
        Position = position;
        Height = height;
        WeightPounds = weightPounds;
        TeamName = teamName;
        League = league;
    }

    public void AddSeasonStat(PlayerSeasonStat stat)
    {
        _seasonStats.Add(stat);
    }
}
