CREATE TABLE custody.holdings
(
    client_id uuid NOT NULL,
    instrument text NOT NULL CHECK (instrument = 'XAU-750'),
    free_fine_mg bigint NOT NULL CHECK (free_fine_mg >= 0),
    version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
    PRIMARY KEY (client_id, instrument)
);

CREATE TABLE credit.facilities
(
    facility_id uuid PRIMARY KEY,
    client_id uuid NOT NULL,
    instrument text NOT NULL CHECK (instrument = 'XAU-750'),
    collateral_fine_mg bigint NOT NULL DEFAULT 0 CHECK (collateral_fine_mg >= 0),
    debt_irr bigint NOT NULL DEFAULT 0 CHECK (debt_irr >= 0),
    status text NOT NULL CHECK (status IN ('Healthy', 'MarginCall', 'LiquidationRequired')),
    version bigint NOT NULL DEFAULT 0 CHECK (version >= 0)
);

CREATE INDEX ix_facilities_client ON credit.facilities (client_id, facility_id);
CREATE INDEX ix_facilities_active ON credit.facilities (status, facility_id)
    WHERE status IN ('Healthy', 'MarginCall');