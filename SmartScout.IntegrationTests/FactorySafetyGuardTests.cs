using System;

namespace SmartScout.IntegrationTests;

public sealed class FactorySafetyGuardTests
{
    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=SmartScoutDb;Username=postgres;Password=123")]
    [InlineData("Host=localhost;Port=5432;Database=smartscoutdb;Username=postgres;Password=123")]
    [InlineData("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123")]
    [InlineData("Host=localhost;Port=5432;Database=;Username=postgres;Password=123")]
    [InlineData("Host=localhost;Port=5432;Database=Bad-Db-Name!;Username=postgres;Password=123")]
    public void EnsureNotLiveDatabase_ShouldRejectLiveOrUnsafeDatabases(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            SmartScoutWebApplicationFactory.EnsureNotLiveDatabase(connectionString));
    }

    [Fact]
    public void EnsureNotLiveDatabase_ShouldAllowIsolatedIntegrationTestDatabase()
    {
        var ex = Record.Exception(() =>
            SmartScoutWebApplicationFactory.EnsureNotLiveDatabase(
                "Host=localhost;Port=5432;Database=SmartScoutIntegrationTestDb;Username=postgres;Password=123"));
        Assert.Null(ex);
    }
}
