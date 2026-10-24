using HtmlAgilityPack;
using SmartScout.Infrastructure.Services.Scraping;
using Xunit;

namespace SmartScout.UnitTests;

public class BasketballReferenceDomParserTests
{
    private readonly BasketballReferenceDomParser _parser = new();

    [Fact]
    public void ParsePerGameTable_ShouldExtractMetricsCorrectly()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <table id='per_game'>
                <tbody>
                    <tr>
                        <th data-stat='year_id'>2023-24</th>
                        <td data-stat='g'>70</td>
                        <td data-stat='mp_per_g'>37.5</td>
                        <td data-stat='pts_per_g'>33.9</td>
                        <td data-stat='ast_per_g'>9.8</td>
                        <td data-stat='trb_per_g'>9.2</td>
                        <td data-stat='stl_per_g'>1.4</td>
                        <td data-stat='blk_per_g'>0.5</td>
                        <td data-stat='fg_pct'>.487</td>
                        <td data-stat='fg3_pct'>.382</td>
                        <td data-stat='ft_pct'>.786</td>
                        <td data-stat='tov_per_g'>4.0</td>
                    </tr>
                </tbody>
            </table>
        </body>
        </html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Act
        var result = _parser.ParsePerGameTable(doc);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(2024));
        var stats = result[2024];
        Assert.Equal(70, stats.GamesPlayed);
        Assert.Equal(37.5m, stats.Minutes);
        Assert.Equal(33.9m, stats.Points);
        Assert.Equal(9.8m, stats.Assists);
        Assert.Equal(9.2m, stats.Rebounds);
        Assert.Equal(1.4m, stats.Steals);
        Assert.Equal(0.5m, stats.Blocks);
    }

    [Fact]
    public void ParsePerGameTable_ShouldDeduplicateTradeSeasonRows_KeepingCombinedStats()
    {
        // Arrange: Player traded mid-season has TOT row first, then Team rows
        var html = @"
        <html>
        <body>
            <table id='per_game'>
                <tbody>
                    <tr>
                        <th data-stat='year_id'>2022-23</th>
                        <td data-stat='g'>60</td>
                        <td data-stat='mp_per_g'>35.0</td>
                        <td data-stat='pts_per_g'>25.0</td>
                    </tr>
                    <tr>
                        <th data-stat='year_id'>2022-23</th>
                        <td data-stat='g'>20</td>
                        <td data-stat='mp_per_g'>30.0</td>
                        <td data-stat='pts_per_g'>20.0</td>
                    </tr>
                </tbody>
            </table>
        </body>
        </html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Act
        var result = _parser.ParsePerGameTable(doc);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(2023));
        Assert.Equal(60, result[2023].GamesPlayed);
        Assert.Equal(25.0m, result[2023].Points);
    }

    [Fact]
    public void ParseAdvancedTable_ShouldUnpackHiddenCommentTable()
    {
        // Arrange: Advanced table placed inside an HTML comment by Basketball-Reference
        var html = @"
        <html>
        <body>
            <div id='all_advanced'>
                <!--
                <table id=""advanced"">
                    <tbody>
                        <tr>
                            <th data-stat=""year_id"">2023-24</th>
                            <td data-stat=""ts_pct"">.617</td>
                            <td data-stat=""per"">28.1</td>
                            <td data-stat=""usg_pct"">36.0</td>
                            <td data-stat=""trb_pct"">13.5</td>
                            <td data-stat=""ast_pct"">44.0</td>
                            <td data-stat=""ws"">12.0</td>
                            <td data-stat=""bpm"">9.9</td>
                        </tr>
                    </tbody>
                </table>
                -->
            </div>
        </body>
        </html>";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Act
        var result = _parser.ParseAdvancedTable(doc);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(2024));
        var adv = result[2024];
        Assert.Equal(0.617m, adv.TrueShootingPct);
        Assert.Equal(28.1m, adv.PlayerEfficiencyRating);
        Assert.Equal(36.0m, adv.UsagePct);
        Assert.Equal(12.0m, adv.WinShares);
        Assert.Equal(9.9m, adv.BoxPlusMinus);
    }
}
