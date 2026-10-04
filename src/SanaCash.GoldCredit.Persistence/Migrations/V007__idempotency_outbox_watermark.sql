CREATE TABLE platform.idempotency_records
(
    client_id uuid NOT NULL,
    operation text NOT NULL,
    idempotency_key text NOT NULL,
    request_fingerprint text NOT NULL,
    is_success boolean NULL,
    response_json jsonb NULL,
    error_code text NULL,
    error_description text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    PRIMARY KEY (client_id, operation, idempotency_key)
);

CREATE TABLE platform.outbox_messages
(
    event_id uuid PRIMARY KEY,
    event_type text NOT NULL,
    aggregate_id text NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL,
    published_at timestamptz NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error text NULL
);

CREATE INDEX ix_outbox_pending_order
    ON platform.outbox_messages (aggregate_id, occurred_at, event_id)
    WHERE published_at IS NULL;

CREATE TABLE platform.evaluator_state
(
    instrument text PRIMARY KEY,
    last_evaluated_minute timestamptz NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);