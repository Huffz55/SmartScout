namespace SmartScout.Infrastructure.Options;

public class ScraperOptions
{
    public const string SectionName = "ScraperSettings";

    public bool Headless { get; set; } = false;
    public int DelayBetweenRequestsMs { get; set; } = 2000;
    public int SelectorTimeoutMs { get; set; } = 5000;
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36";
    public int MaxSuffixAttempts { get; set; } = 3;
    public bool EnableBackgroundSync { get; set; } = false;
}
