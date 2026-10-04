ALTER TABLE pricing.price_ticks SET
(
    timescaledb.compress = true,
    timescaledb.compress_segmentby = 'instrument',
    timescaledb.compress_orderby = 'ts DESC'
);

SELECT add_compression_policy('pricing.price_ticks', INTERVAL '7 days', if_not_exists => TRUE);
SELECT add_retention_policy('pricing.price_ticks', INTERVAL '30 days', if_not_exists => TRUE);
SELECT add_retention_policy('pricing.price_candles_1m', INTERVAL '2 years', if_not_exists => TRUE);