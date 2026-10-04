CREATE MATERIALIZED VIEW pricing.price_candles_1m
WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
SELECT time_bucket(INTERVAL '1 minute', ts) AS bucket,
       instrument,
       first(price_irr_per_gram, ts) AS open_irr_per_gram,
       max(price_irr_per_gram) AS high_irr_per_gram,
       min(price_irr_per_gram) AS low_irr_per_gram,
       last(price_irr_per_gram, ts) AS close_irr_per_gram,
       count(*) AS tick_count
FROM pricing.price_ticks
GROUP BY bucket, instrument
WITH NO DATA;

SELECT add_continuous_aggregate_policy('pricing.price_candles_1m',
    start_offset => INTERVAL '7 days',
    end_offset => INTERVAL '5 seconds',
    schedule_interval => INTERVAL '30 seconds',
    if_not_exists => TRUE);