using System;
using System.Collections.Generic;
using SmartScout.Application.DTOs;
using SmartScout.Application.Services;
using SmartScout.Domain.Entities;
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

    [Fact]
    public void ParseAcbParteMedicoRss_ShouldExtractOutQuestionableRecoveredAndSkipAvailable()
    {
        string rss = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:content=""http://purl.org/rss/1.0/modules/content/"">
  <channel>
    <item>
      <title>Novedades y parte médico de la Jornada 8 de la Liga Endesa 2026-27</title>
      <pubDate>Fri, 13 Nov 2026 20:00:00 +0000</pubDate>
      <content:encoded><![CDATA[
        <p>• Barça: Kevin Punter (esguince del tobillo izquierdo) sigue siendo baja. Por otro lado, se espera que Umoja Gibson pueda jugar, mientras que Juan Núñez es duda.</p>
        <p>• Kids&amp;Us Manresa: Chibuzo Agbo se suma a la lista de bajas. La buena noticia llega de la mano de Timmy Allen, que regresa tras superar su lesión.</p>
      ]]></content:encoded>
    </item>
  </channel>
</rss>";

        var acbCandidates = new List<PlayerInjurySyncService.PlayerCandidate>
        {
            new(Guid.NewGuid(), "Kevin Punter", "kevin punter", "kevin punter", "ACB", "Barca", new HashSet<string> { "ACB" }),
            new(Guid.NewGuid(), "Umoja Gibson", "umoja gibson", "umoja gibson", "ACB", "Barca", new HashSet<string> { "ACB" }),
            new(Guid.NewGuid(), "Juan Nunez", "juan nunez", "juan nunez", "ACB", "Barca", new HashSet<string> { "ACB" }),
            new(Guid.NewGuid(), "Chibuzo Agbo", "chibuzo agbo", "chibuzo agbo", "ACB", "Manresa", new HashSet<string> { "ACB" }),
            new(Guid.NewGuid(), "Timmy Allen", "timmy allen", "timmy allen", "ACB", "Manresa", new HashSet<string> { "ACB" })
        };

        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var records = PlayerInjurySyncService.ParseAcbParteMedicoRss(rss, acbCandidates, utcNow);

        var punter = Assert.Single(records, r => r.PlayerName == "kevin punter");
        Assert.Equal("Out", punter.Status);
        Assert.Equal("Ankle", punter.InjuryType);
        Assert.Equal("ACB", punter.PreferredLeague);

        var nunez = Assert.Single(records, r => r.PlayerName == "juan nunez");
        Assert.Equal("Questionable", nunez.Status);

        var agbo = Assert.Single(records, r => r.PlayerName == "chibuzo agbo");
        Assert.Equal("Out", agbo.Status);

        var allen = Assert.Single(records, r => r.PlayerName == "timmy allen");
        Assert.Equal("Recovered", allen.Status);

        Assert.DoesNotContain(records, r => r.PlayerName == "umoja gibson");
    }

    [Fact]
    public void ParseEuropeanNewsItems_ShouldMatchSubjectInjuriesAndRejectNoiseHeadlines()
    {
        var euroCandidates = new List<PlayerInjurySyncService.PlayerCandidate>
        {
            new(Guid.NewGuid(), "Nicolo Melli", "nicolo melli", "nicolo melli", "BSL", "Fenerbahce", new HashSet<string> { "BSL", "EuroLeague" }),
            new(Guid.NewGuid(), "Braxton Key", "braxton key", "braxton key", "BSL", "Fenerbahce", new HashSet<string> { "BSL", "EuroLeague" }),
            new(Guid.NewGuid(), "Cedi Osman", "cedi osman", "cedi osman", "EuroCup", "PAOK", new HashSet<string> { "EuroCup", "GBL" }),
            new(Guid.NewGuid(), "Luka Scuka", "luka scuka", "luka scuka", "EuroCup", "Manresa", new HashSet<string> { "EuroCup", "ACB" })
        };

        var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var items = new[]
        {
            (
                Title: "Double blow for Fenerbahce: Nicolo Melli faces surgery, Braxton Key suffers dislocated finger",
                Body: "Fenerbahce's derby win was overshadowed by severe injury news involving Nicolo Melli and Braxton Key.",
                PublishedAt: utcNow.AddHours(-6)
            ),
            (
                Title: "Osman undergoes surgery and will be sidelined for three months",
                Body: "Cedi Osman underwent surgery, with a recovery time estimated at three months.",
                PublishedAt: utcNow.AddDays(-9)
            ),
            (
                Title: "Luka Doncic FIBA teammate returns to EuroCup, signs with Manresa",
                Body: "Manresa welcomes Luka Scuka, while Turk Telekom sidelines Tony Taylor.",
                PublishedAt: utcNow.AddDays(-5)
            )
        };

        var records = PlayerInjurySyncService.ParseEuropeanNewsItems(items, "EuroCup", "BasketballSphere EU", euroCandidates, utcNow);

        var melli = Assert.Single(records, r => r.PlayerName == "nicolo melli");
        Assert.Equal("Out", melli.Status);
        Assert.Equal("Surgery", melli.InjuryType);

        var key = Assert.Single(records, r => r.PlayerName == "braxton key");
        Assert.Equal("Out", key.Status);
        Assert.Equal("Finger", key.InjuryType);

        var osman = Assert.Single(records, r => r.PlayerName == "cedi osman");
        Assert.Equal("Out", osman.Status);
        Assert.Equal("Surgery", osman.InjuryType);

        Assert.DoesNotContain(records, r => r.PlayerName == "luka scuka");
    }

    [Fact]
    public void ParseLocalizedWikitextBio_ShouldExtractSpanishItalianRussianFieldsAndMatchCyrillicTitle()
    {
        string esWikitext = @"
{{Ficha de deportista
| nacionalidad = {{bandera|España}}
| fecha de nacimiento = {{fecha de nacimiento|15|04|2002}}
| altura = 1,98 m
| peso = 92 kg
}}";
        var esBio = WikipediaBioEnrichmentService.ParseLocalizedWikitextBio(esWikitext);
        Assert.Equal(new DateTime(2002, 4, 15, 0, 0, 0, DateTimeKind.Utc), esBio.BirthDate);
        Assert.Equal("ESP", esBio.Nationality);
        Assert.Equal(198, esBio.HeightCm);
        Assert.Equal(92, esBio.WeightKg);

        string ruWikitext = @"
{{Баскетболист
| Гражданство = {{Флагификация|Россия}}
| Дата рождения = {{ДатаРождения|16|12|1988}}
| Рост = 198 см
| Вес = 86 кг
}}";
        var ruBio = WikipediaBioEnrichmentService.ParseLocalizedWikitextBio(ruWikitext);
        Assert.Equal(new DateTime(1988, 12, 16, 0, 0, 0, DateTimeKind.Utc), ruBio.BirthDate);
        Assert.Equal("RUS", ruBio.Nationality);
        Assert.Equal(198, ruBio.HeightCm);
        Assert.Equal(86, ruBio.WeightKg);

        var shved = new Player(1, "Alexey", "Shved", "SG", null, null, "Zenit", "VTB");
        Assert.True(WikipediaBioEnrichmentService.NameReasonablyMatches(shved, "Швед, Алексей Викторович"));
    }
}
