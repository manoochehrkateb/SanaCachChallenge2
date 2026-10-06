using Microsoft.EntityFrameworkCore;
using SanaCash.GoldCredit.Persistence.Credit;
using SanaCash.GoldCredit.Persistence.Custody;
using SanaCash.GoldCredit.Persistence.Pricing;

namespace SanaCash.GoldCredit.Persistence.Common;

public class GoldCreditDbContext(DbContextOptions<GoldCreditDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreditFacilityEntity>(entity =>
        {
            entity.ToTable("facilities", "credit");
            entity.HasKey(item => item.FacilityId);
            entity.Property(item => item.FacilityId).HasColumnName("facility_id");
            entity.Property(item => item.ClientId).HasColumnName("client_id");
            entity.Property(item => item.Instrument).HasColumnName("instrument");
            entity.Property(item => item.CollateralFineMg).HasColumnName("collateral_fine_mg");
            entity.Property(item => item.DebtIrr).HasColumnName("debt_irr");
            entity.Property(item => item.Status).HasColumnName("status");
            entity.Property(item => item.Version).HasColumnName("version").IsConcurrencyToken();
        });

        modelBuilder.Entity<MarginEventEntity>(entity =>
        {
            entity.ToTable("margin_events", "credit");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.FacilityId).HasColumnName("facility_id");
            entity.Property(item => item.EventType).HasColumnName("event_type");
            entity.Property(item => item.EpisodeNumber).HasColumnName("episode_number");
            entity.Property(item => item.OccurredAt).HasColumnName("occurred_at");
            entity.Property(item => item.Evidence).HasColumnName("evidence").HasColumnType("jsonb");
        });

        modelBuilder.Entity<CustodyHoldingEntity>(entity =>
        {
            entity.ToTable("holdings", "custody");
            entity.HasKey(item => new { item.ClientId, item.Instrument });
            entity.Property(item => item.ClientId).HasColumnName("client_id");
            entity.Property(item => item.Instrument).HasColumnName("instrument");
            entity.Property(item => item.FreeFineMg).HasColumnName("free_fine_mg");
            entity.Property(item => item.Version).HasColumnName("version").IsConcurrencyToken();
        });

        modelBuilder.Entity<RejectedPriceTickEntity>(entity =>
        {
            entity.ToTable("rejected_price_ticks", "pricing");
            entity.HasKey(item => new { item.Id, item.ReceivedAt });
            entity.Property(item => item.Id).HasColumnName("id");
            entity.Property(item => item.ReceivedAt).HasColumnName("received_at");
            entity.Property(item => item.Instrument).HasColumnName("instrument");
            entity.Property(item => item.Sequence).HasColumnName("sequence");
            entity.Property(item => item.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(item => item.RejectionReason).HasColumnName("rejection_reason");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages", "platform");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.EventType).HasColumnName("event_type");
            entity.Property(item => item.AggregateId).HasColumnName("aggregate_id");
            entity.Property(item => item.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(item => item.OccurredAt).HasColumnName("occurred_at");
            entity.Property(item => item.PublishedAt).HasColumnName("published_at");
            entity.Property(item => item.Attempts).HasColumnName("attempts");
            entity.Property(item => item.LastError).HasColumnName("last_error");
        });

        modelBuilder.Entity<PriceTickEntity>(entity =>
        {
            entity.ToTable("price_ticks", "pricing");
            entity.HasKey(item => new { item.Instrument, item.Sequence, item.Timestamp });
            entity.Property(item => item.Timestamp).HasColumnName("ts");
            entity.Property(item => item.Instrument).HasColumnName("instrument");
            entity.Property(item => item.Sequence).HasColumnName("sequence");
            entity.Property(item => item.PriceIrrPerGram).HasColumnName("price_irr_per_gram");
        });

        modelBuilder.Entity<PriceCandleEntity>(entity =>
        {
            entity.ToTable("price_candles_1m", "pricing");
            entity.HasKey(item => new { item.Instrument, item.Bucket });
            entity.Property(item => item.Bucket).HasColumnName("bucket");
            entity.Property(item => item.Instrument).HasColumnName("instrument");
            entity.Property(item => item.OpenIrrPerGram).HasColumnName("open_irr_per_gram");
            entity.Property(item => item.HighIrrPerGram).HasColumnName("high_irr_per_gram");
            entity.Property(item => item.LowIrrPerGram).HasColumnName("low_irr_per_gram");
            entity.Property(item => item.CloseIrrPerGram).HasColumnName("close_irr_per_gram");
            entity.Property(item => item.TickCount).HasColumnName("tick_count");
        });

        base.OnModelCreating(modelBuilder);
    }
}
