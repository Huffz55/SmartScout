namespace SmartScout.Infrastructure.Options;

public class ScraperOptions
{
    public const string SectionName = "ScraperSettings";

    public bool Headless { get; set; } = false;
    // Basketball-Reference enforces a strict 20 requests/minute limit (minimum 3000ms between requests; 3500ms = ~17 req/min safe buffer)
    public int DelayBetweenRequestsMs { get; set; } = 3500;
    public int SelectorTimeoutMs { get; set; } = 5000;
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36";
    public int MaxSuffixAttempts { get; set; } = 3;
    public bool EnableBackgroundSync { get; set; } = false;
    public bool EnableDomesticLeagueFullSweep { get; set; } = false;

    // Multi-season European basketball ingestion from 2018-19 (2019) to 2025-26 (2026) in chronological order
    public int[] EuroSeasons { get; set; } = new[] { 2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026 };
    public int EuroMaxConcurrentRequests { get; set; } = 2;
    public int EuroDelayBetweenWavesMs { get; set; } = 1200;
    public bool EnableEuroLiveApiBoxscores { get; set; } = false;
    public bool EnableNbaPlayerBioScraping { get; set; } = false;
}
