using System.Reflection;
using Npgsql;

namespace SanaCash.GoldCredit.Persistence.Migrations;

public sealed class MigrationRunner(NpgsqlDataSource dataSource)
{
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var setup = new NpgsqlCommand(
            "CREATE SCHEMA IF NOT EXISTS platform; " +
            "CREATE TABLE IF NOT EXISTS platform.schema_migrations " +
            "(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());",
            connection))
        {
            await setup.ExecuteNonQueryAsync(cancellationToken);
        }

        var migrations = GetMigrations();
        if (migrations.Count == 0)
        {
            throw new InvalidOperationException("No embedded SQL migrations were found.");
        }

        foreach (var migration in migrations)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var check = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM platform.schema_migrations WHERE version = @version);",
                connection,
                transaction))
            {
                check.Parameters.AddWithValue("version", migration.Version);
                if ((bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }
            }

            await using (var command = new NpgsqlCommand(migration.Sql, connection, transaction))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record = new NpgsqlCommand(
                "INSERT INTO platform.schema_migrations (version) VALUES (@version);",
                connection,
                transaction))
            {
                record.Parameters.AddWithValue("version", migration.Version);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<Migration> GetMigrations()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var migrations = new List<Migration>();
        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
        {
            var markerIndex = resourceName.LastIndexOf(".Migrations.", StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Migration resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream);
            migrations.Add(new Migration(resourceName[(markerIndex + 12)..^4], reader.ReadToEnd()));
        }

        return migrations.OrderBy(migration => migration.Version, StringComparer.Ordinal).ToArray();
    }

    private sealed record Migration(string Version, string Sql);
}