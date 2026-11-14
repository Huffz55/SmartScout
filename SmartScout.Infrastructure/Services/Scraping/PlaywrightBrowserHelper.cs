using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace SmartScout.Infrastructure.Services.Scraping;

public static class PlaywrightBrowserHelper
{
    /// <summary>
    /// Launches a Chromium browser using system-installed channels (Google Chrome, Microsoft Edge)
    /// before falling back to Playwright's bundled Chromium build.
    /// This prevents CDN download timeouts for bundled browsers on environments where Chrome or Edge is already installed.
    /// </summary>
    public static async Task<IBrowser> LaunchBrowserWithFallbackAsync(
        IPlaywright playwright,
        bool headless,
        ILogger? logger = null)
    {
        string[] channels = { "chrome", "msedge" };

        foreach (var channel in channels)
        {
            try
            {
                logger?.LogInformation("Attempting to launch Playwright using system browser channel '{Channel}' (Headless: {Headless})...", channel, headless);
                return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless,
                    Channel = channel
                });
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to launch browser with channel '{Channel}'. Trying next fallback...", channel);
            }
        }

        string[] explicitPaths =
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };

        foreach (var exePath in explicitPaths)
        {
            if (!File.Exists(exePath)) continue;
            try
            {
                return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless,
                    ExecutablePath = exePath
                });
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to launch browser at '{ExecutablePath}'.", exePath);
            }
        }

        // Final fallback: default Playwright bundled Chromium
        logger?.LogInformation("Falling back to default bundled Playwright Chromium launch...");
        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless
        });
    }
}
