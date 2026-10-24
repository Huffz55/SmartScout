using System;
using System.Collections.Generic;

namespace SmartScout.Domain.Entities;

public class Player
{
    public Guid Id { get; private set; }
    public int ExternalApiId { get; private set; } // e.g. from balldontlie or euroleague
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Position { get; private set; }
    public string? Height { get; private set; }
    public int? WeightPounds { get; private set; }
    public string? TeamName { get; private set; }
    public string League { get; private set; } = string.Empty;
    public string? Nationality { get; private set; }
    public DateTime? BirthDate { get; private set; }

    private readonly List<PlayerSeasonStat> _seasonStats = new();
    public IReadOnlyCollection<PlayerSeasonStat> SeasonStats => _seasonStats.AsReadOnly();

    // EF Core constructor
    private Player() { }

    public Player(
        int externalApiId, 
        string firstName, 
        string lastName, 
        string? position, 
        string? height, 
        int? weightPounds, 
        string? teamName, 
        string league, 
        string? nationality = null, 
        DateTime? birthDate = null)
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
        Nationality = nationality;
        BirthDate = birthDate;
    }

    public void UpdateDetails(
        string firstName, 
        string lastName, 
        string? position, 
        string? height, 
        int? weightPounds, 
        string? teamName, 
        string league, 
        string? nationality = null, 
        DateTime? birthDate = null)
    {
        FirstName = firstName;
        LastName = lastName;
        Position = position;
        Height = height;
        WeightPounds = weightPounds;
        TeamName = teamName;
        League = league;
        Nationality = nationality ?? Nationality;
        BirthDate = birthDate ?? BirthDate;
    }

    public void AddSeasonStat(PlayerSeasonStat stat)
    {
        _seasonStats.Add(stat);
    }
}
