using System;
using System.Collections.Generic;
using SmartScout.Application.DTOs;
using SmartScout.Application.Services;
using SmartScout.Infrastructure.Options;
using SmartScout.Infrastructure.Services;
using Xunit;

namespace SmartScout.UnitTests;

public class LiveSyncAndInjuryFeedTests
{
    [Fact]
    public void ParseCoversInjuriesHtml_ShouldExtractFullNameFromSlugStatusInjuryDateAndNotes()
    {
        string html = @"
<table class=""covers-CoversSeasonInjuries-headerTable"">
  <tbody>
    <tr>
      <td class=""covers-CoversSeasonInjuries-teamNameColumn"">
        <div class=""covers-CoversMatchups-teamName"">
          <a href=""/sport/basketball/ncaab/teams/main/alabama-crimson-tide"">Alabama<span>Crimson Tide</span></a>
        </div>
      </td>
    </tr>
  </tbody>
</table>
<table class=""table table-sm covers-CoversSeasonInjuries-table"">
  <tbody>
    <tr>
      <td><a href=""/sport/basketball/ncaab/players/374839/jamarion-davis-fleming"" class=""player-link"">J. Davis-Fleming</a></td>
      <td>F</td>
      <td><b>Questionable - Leg</b><br>(Thu, Nov 12)</td>
      <td></td>
    </tr>
    <tr class=""collapse"" id=""injuryCollapseAALA0"">
      <td colspan=""4"">
        <div class=""covers-CoversMatchups-injuryCopy"">
          Davis-Fleming is dealing with a leg injury and is uncertain to take the court.
        </div>
      </td>
    </tr>
  </tbody>
</table>";

        var utcNow = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
        var records = PlayerInjurySyncService.ParseCoversInjuriesHtml(html, "NCAAD1", "Covers NCAAB", utcNow);

        var rec = Assert.Single(records);
        Assert.Equal("jamarion davis fleming", rec.PlayerName);
        Assert.Equal("NCAAD1", rec.PreferredLeague);
        Assert.Equal("Alabama Crimson Tide", rec.TeamName);
        Assert.Equal("Leg", rec.InjuryType);
        Assert.Equal("Questionable", rec.Status);
        Assert.Equal(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), rec.ReportedAt);
        Assert.Contains("leg injury", rec.Notes);
    }

    [Fact]
    public void ParseCoversInjuriesHtml_ShouldWrapYearBackwardWhenNovemberDateIsReadInJanuary()
    {
        string html = @"
<table>
  <tbody>
    <tr>
      <td><a href=""/sport/basketball/nba/players/101/jane-doe"" class=""player-link"">J. Doe</a></td>
      <td>G</td>
      <td><b>Out - Ankle</b><br>(Fri, Nov 20)</td>
      <td></td>
    </tr>
  </tbody>
</table>";

        var janNow = new DateTime(2027, 1, 10, 8, 0, 0, DateTimeKind.Utc);
        var records = PlayerInjurySyncService.ParseCoversInjuriesHtml(html, "NBA", "Covers NBA", janNow);

        var rec = Assert.Single(records);
        Assert.Equal(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), rec.ReportedAt);
    }

    [Fact]
    public void EvaluateSchedule_ShouldSkipFreshCheckpointsAndScheduleExactRemainingDelay()
    {
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        var options = new ScraperOptions
        {
            EnableLiveInjuryAutoSync = true,
            InjurySyncIntervalHours = 4,
            EnableBackgroundSync = true,
            CurrentSeasonStatRefreshHours = 24
        };

        var decision = LiveSyncCoordinator.EvaluateSchedule(
            now,
            lastInjuryAt: now.AddHours(-2),
            lastStatAt: now.AddHours(-6),
            options);

        Assert.False(decision.RunInjurySync);
        Assert.False(decision.RunStatSync);
        Assert.Equal(TimeSpan.FromHours(2), decision.NextDelayIfClean);
    }

    [Fact]
    public void EvaluateSchedule_ShouldTriggerInjuryImmediatelyAndLiveSeasonOnlyWhenStale()
    {
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        var options = new ScraperOptions
        {
            EnableLiveInjuryAutoSync = true,
            InjurySyncIntervalHours = 4,
            EnableBackgroundSync = true,
            CurrentSeasonStatRefreshHours = 24
        };

        var decision = LiveSyncCoordinator.EvaluateSchedule(
            now,
            lastInjuryAt: null,
            lastStatAt: now.AddHours(-25),
            options);

        Assert.True(decision.RunInjurySync);
        Assert.True(decision.RunStatSync);
        Assert.False(decision.StatSyncAllBaseline);
        Assert.Equal(TimeSpan.FromHours(4), decision.NextDelayIfClean);
    }

    [Fact]
    public void ScoutingCacheState_AndSleeperClone_ShouldIsolateCacheEntriesAndIncrementVersion()
    {
        var cacheState = new ScoutingCacheState();
        long v1 = cacheState.Version;
        long v2 = cacheState.BumpVersion();
        Assert.Equal(v1 + 1, v2);
        Assert.Equal(v2, cacheState.Version);

        var original = new SleeperPlayerDto
        {
            FullName = "Sample Guard",
            ActiveInjuryStatus = null,
            ScoutingReasons = new List<string> { "Spacing" }
        };

        var copy = original.Clone();
        copy.ActiveInjuryStatus = "Out";
        copy.ScoutingReasons.Add("Extra");

        Assert.Null(original.ActiveInjuryStatus);
        Assert.Single(original.ScoutingReasons);
    }
}
