CREATE TABLE credit.margin_evaluations
(
    facility_id uuid NOT NULL REFERENCES credit.facilities (facility_id),
    minute timestamptz NOT NULL,
    close_irr_per_gram bigint NOT NULL CHECK (close_irr_per_gram > 0),
    debt_irr bigint NOT NULL CHECK (debt_irr >= 0),
    collateral_fine_mg bigint NOT NULL CHECK (collateral_fine_mg >= 0),
    collateral_value_irr bigint NOT NULL CHECK (collateral_value_irr >= 0),
    ltv_bps numeric(39, 0) NOT NULL CHECK (ltv_bps >= 0),
    is_ltv_infinite boolean NOT NULL DEFAULT false,
    PRIMARY KEY (facility_id, minute)
);

CREATE INDEX ix_margin_evaluations_minute ON credit.margin_evaluations (minute, facility_id);

CREATE TABLE credit.margin_events
(
    event_id uuid PRIMARY KEY,
    facility_id uuid NOT NULL REFERENCES credit.facilities (facility_id),
    event_type text NOT NULL CHECK (event_type IN ('MarginCallIssued', 'MarginCallCured', 'LiquidationRequired')),
    episode_number integer NOT NULL CHECK (episode_number > 0),
    occurred_at timestamptz NOT NULL,
    evidence jsonb NOT NULL,
    UNIQUE (facility_id, event_type, episode_number)
);

CREATE INDEX ix_margin_events_facility_time ON credit.margin_events (facility_id, occurred_at DESC);