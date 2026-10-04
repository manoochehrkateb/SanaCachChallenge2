CREATE TABLE pricing.price_tick_keys
(
    instrument text NOT NULL,
    sequence bigint NOT NULL,
    payload_hash bytea NOT NULL,
    first_seen_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (instrument, sequence)
);

CREATE TABLE pricing.price_ticks
(
    ts timestamptz NOT NULL,
    instrument text NOT NULL,
    sequence bigint NOT NULL,
    price_irr_per_gram bigint NOT NULL CHECK (price_irr_per_gram > 0),
    PRIMARY KEY (instrument, sequence, ts),
    FOREIGN KEY (instrument, sequence)
        REFERENCES pricing.price_tick_keys (instrument, sequence)
);

SELECT create_hypertable('pricing.price_ticks', 'ts',
    chunk_time_interval => INTERVAL '1 day', if_not_exists => TRUE);

CREATE INDEX ix_price_ticks_latest
    ON pricing.price_ticks (instrument, ts DESC);

CREATE TABLE pricing.rejected_price_ticks
(
    received_at timestamptz NOT NULL,
    instrument text NULL,
    sequence bigint NULL,
    payload jsonb NOT NULL,
    rejection_reason text NOT NULL
);

SELECT create_hypertable('pricing.rejected_price_ticks', 'received_at',
    chunk_time_interval => INTERVAL '7 days', if_not_exists => TRUE);

CREATE INDEX ix_rejected_price_ticks_received_at
    ON pricing.rejected_price_ticks (received_at DESC);