using System;

namespace SmartScout.Domain.Entities;

public class IngestionJobLog
{
    public Guid Id { get; private set; }
    public string LeagueCode { get; private set; } = string.Empty;
    public int Season { get; private set; }
    public string Status { get; private set; } = "Pending"; // "Pending", "Running", "Completed", "Failed"
    public int LastScrapedPage { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    private IngestionJobLog() { }

    public IngestionJobLog(
        string leagueCode,
        int season,
        string status = "Running",
        int lastScrapedPage = 0,
        string? errorMessage = null)
    {
        Id = Guid.NewGuid();
        LeagueCode = leagueCode;
        Season = season;
        Status = status;
        LastScrapedPage = lastScrapedPage;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateProgress(int lastScrapedPage, string status = "Running")
    {
        LastScrapedPage = lastScrapedPage;
        Status = status;
        ErrorMessage = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompleted(int finalScrapedPage)
    {
        LastScrapedPage = finalScrapedPage;
        Status = "Completed";
        ErrorMessage = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string errorMessage, int lastScrapedPage = 0)
    {
        LastScrapedPage = lastScrapedPage;
        Status = "Failed";
        ErrorMessage = errorMessage;
        UpdatedAt = DateTime.UtcNow;
    }
}
