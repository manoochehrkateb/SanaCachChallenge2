using Xunit;

namespace SanaCash.GoldCredit.IntegrationTests;

public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SANACASH_TEST_CONNECTION")))
        {
            Skip = "Set SANACASH_TEST_CONNECTION to run PostgreSQL/TimescaleDB integration tests.";
        }
    }
}