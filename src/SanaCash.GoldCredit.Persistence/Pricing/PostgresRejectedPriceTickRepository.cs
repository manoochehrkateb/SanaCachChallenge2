using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Persistence.Common;

namespace SanaCash.GoldCredit.Persistence.Pricing;

public class PostgresRejectedPriceTickRepository(
    GoldCreditDbContext context,
    PostgresSession session) : GenericRepository<RejectedPriceTickEntity>(context, session), IRejectedPriceTickRepository
{
    public PostgresRejectedPriceTickRepository(PostgresSession session)
        : this(CreateContext(session), session)
    {
    }

    public async Task AddAsync(RejectedPriceTick tick, CancellationToken cancellationToken = default)
    {
        var entity = new RejectedPriceTickEntity
        {
            Id = tick.Id,
            ReceivedAt = tick.Timestamp.ToUniversalTime(),
            Instrument = tick.Instrument,
            Sequence = tick.Sequence,
            Payload = JsonSerializer.Serialize(new
            {
                tick.Instrument,
                tick.Sequence,
                tick.Price,
                TimestampUtc = tick.Timestamp,
                tick.RawPayload
            }),
            RejectionReason = tick.Reason.ToString()
        };

        await AddAsync(entity, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    private static GoldCreditDbContext CreateContext(PostgresSession session)
    {
        var context = new GoldCreditDbContext(
            new DbContextOptionsBuilder<GoldCreditDbContext>()
                .UseNpgsql(session.Connection)
                .Options);
        context.Database.UseTransaction(session.Transaction);
        return context;
    }
}
