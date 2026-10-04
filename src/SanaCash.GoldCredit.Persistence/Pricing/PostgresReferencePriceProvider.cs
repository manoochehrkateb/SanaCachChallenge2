using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Domain.Shared;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public sealed class PostgresReferencePriceProvider(NpgsqlDataSource dataSource) : IReferencePriceProvider
{
    public async Task<ReferencePriceQuote?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT ts, price_irr_per_gram FROM pricing.price_ticks " +
            "WHERE instrument = @instrument ORDER BY ts DESC LIMIT 1;",
            connection);
        command.Parameters.AddWithValue("instrument", instrument.Code);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var price = ReferencePrice.Create(reader.GetInt64(1));
        if (price.IsFailure)
        {
            throw new InvalidOperationException("Persisted reference price is invalid.");
        }

        return new ReferencePriceQuote(price.Value, reader.GetFieldValue<DateTimeOffset>(0));
    }
}