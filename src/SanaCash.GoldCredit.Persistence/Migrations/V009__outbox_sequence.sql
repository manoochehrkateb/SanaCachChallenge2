ALTER TABLE platform.outbox_messages
ALTER TABLE platform.outbox_messages
    ADD COLUMN outbox_sequence bigint;

CREATE SEQUENCE platform.outbox_messages_outbox_sequence_seq;

UPDATE platform.outbox_messages
SET outbox_sequence = nextval('platform.outbox_messages_outbox_sequence_seq')
WHERE outbox_sequence IS NULL;

ALTER SEQUENCE platform.outbox_messages_outbox_sequence_seq
    OWNED BY platform.outbox_messages.outbox_sequence;

ALTER TABLE platform.outbox_messages
    ALTER COLUMN outbox_sequence SET DEFAULT nextval('platform.outbox_messages_outbox_sequence_seq');

ALTER TABLE platform.outbox_messages
    ALTER COLUMN outbox_sequence SET NOT NULL;

CREATE UNIQUE INDEX ux_outbox_messages_sequence
    ON platform.outbox_messages (outbox_sequence);
