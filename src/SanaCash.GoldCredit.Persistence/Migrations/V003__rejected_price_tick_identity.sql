ALTER TABLE pricing.rejected_price_ticks
ALTER TABLE pricing.rejected_price_ticks
    ADD COLUMN id uuid NOT NULL DEFAULT gen_random_uuid();

ALTER TABLE pricing.rejected_price_ticks
    ADD CONSTRAINT pk_rejected_price_ticks
    PRIMARY KEY (id, received_at);
