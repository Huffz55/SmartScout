using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartScout.Application.Interfaces;
using SmartScout.Infrastructure.Data;

namespace SmartScout.IntegrationTests;

[CollectionDefinition("PostgresIntegration")]
public sealed class PostgresIntegrationCollection : ICollectionFixture<SmartScoutWebApplicationFactory>
{
}

public sealed partial class SmartScoutWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    static SmartScoutWebApplicationFactory()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public const string TestApiKey = "integration-test-api-key";
    private const string DefaultIsolatedTestConnection =
        "Host=localhost;Port=5432;Database=SmartScoutIntegrationTestDb;Username=postgres;Password=123";

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex SafeDatabaseNameRegex();

    public string ConnectionString { get; }

    public SmartScoutWebApplicationFactory()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTSCOUT_TEST_CONNECTION");
        ConnectionString = string.IsNullOrWhiteSpace(configured)
            ? DefaultIsolatedTestConnection
            : configured;

        EnsureNotLiveDatabase(ConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Database:AutoMigrateOnStartup"] = "false",
                ["Hosting:EnableHttpsRedirection"] = "false",
                ["ScraperSettings:EnableBackgroundSync"] = "false",
                ["ScraperSettings:EnableLiveInjuryAutoSync"] = "false",
                ["Authentication:ApiKey"] = TestApiKey
            });
        });
    }

    public async Task InitializeAsync()
    {
        EnsureNotLiveDatabase(ConnectionString);
        await EnsureTestDatabaseExistsAsync(ConnectionString);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartScoutDbContext>();
        await db.Database.MigrateAsync();
        await ResetDatabaseAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        EnsureNotLiveDatabase(ConnectionString);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartScoutDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "ScoutBoardEntries",
                "ScoutBoards",
                "PlayerInjuries",
                "SeasonStats",
                "Players",
                "IngestionJobLogs"
            RESTART IDENTITY CASCADE;
            """);

        scope.ServiceProvider.GetService<IScoutingCacheState>()?.BumpVersion();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
    }

    internal static void EnsureNotLiveDatabase(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        string dbName = builder.Database ?? string.Empty;

        if (string.IsNullOrWhiteSpace(dbName)
            || string.Equals(dbName, "SmartScoutDb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dbName, "postgres", StringComparison.OrdinalIgnoreCase)
            || !SafeDatabaseNameRegex().IsMatch(dbName))
        {
            throw new InvalidOperationException(
                $"Refusing to execute integration tests against invalid or live database '{dbName}'.");
        }
    }

    private static async Task EnsureTestDatabaseExistsAsync(string connectionString)
    {
        EnsureNotLiveDatabase(connectionString);
        var targetBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        string targetDbName = targetBuilder.Database!;
        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        await using var conn = new NpgsqlConnection(adminBuilder.ConnectionString);
        await conn.OpenAsync();

        await using var existsCmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @dbName;", conn);
        existsCmd.Parameters.AddWithValue("dbName", targetDbName);
        var exists = await existsCmd.ExecuteScalarAsync();
        if (exists != null)
        {
            return;
        }

        // targetDbName is validated against SafeDatabaseNameRegex (alphanumeric/underscore only)
        await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{targetDbName}\";", conn);
        await createCmd.ExecuteNonQueryAsync();
    }
}
