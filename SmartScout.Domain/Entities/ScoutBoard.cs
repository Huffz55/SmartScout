using System;
using System.Collections.Generic;

namespace SmartScout.Domain.Entities;

public class ScoutBoard
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string OwnerName { get; private set; } = "Lead Scout";
    public DateTime CreatedAt { get; private set; }

    private readonly List<ScoutBoardEntry> _entries = new();
    public IReadOnlyCollection<ScoutBoardEntry> Entries => _entries.AsReadOnly();

    private ScoutBoard() { }

    public ScoutBoard(string name, string? ownerName = null)
    {
        Id = Guid.NewGuid();
        Name = string.IsNullOrWhiteSpace(name) ? "Custom Target Board" : name.Trim();
        if (Name.Length > 100) Name = Name[..100];
        OwnerName = string.IsNullOrWhiteSpace(ownerName) ? "Lead Scout" : ownerName.Trim();
        if (OwnerName.Length > 80) OwnerName = OwnerName[..80];
        CreatedAt = DateTime.UtcNow;
    }
}

public class ScoutBoardEntry
{
    public Guid Id { get; private set; }
    public Guid BoardId { get; private set; }
    public Guid PlayerId { get; private set; }
    public string? Notes { get; private set; }
    public DateTime AddedAt { get; private set; }

    public ScoutBoard? Board { get; private set; }
    public Player? Player { get; private set; }

    private ScoutBoardEntry() { }

    public ScoutBoardEntry(Guid boardId, Guid playerId, string? notes = null)
    {
        Id = Guid.NewGuid();
        BoardId = boardId;
        PlayerId = playerId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        AddedAt = DateTime.UtcNow;
    }

    public void SetNotes(string? notes)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
