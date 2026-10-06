using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Domain.Shared;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public class PostgresReferencePriceProvider(
    GoldCreditDbContext context,
    PostgresSession session) : GenericRepository<PriceTickEntity>(context, session), IReferencePriceProvider
{
    public async Task<ReferencePriceQuote?> GetLatestAsync(
        Instrument instrument,
        CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .Where(item => item.Instrument == instrument.Code)
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var price = ReferencePrice.Create(entity.PriceIrrPerGram);
        if (price.IsFailure)
        {
            throw new InvalidOperationException("Persisted reference price is invalid.");
        }

        return new ReferencePriceQuote(price.Value, entity.Timestamp);
    }
}
