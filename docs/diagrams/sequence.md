# Concurrency And Outbox Sequence

```mermaid
sequenceDiagram
    participant A as Release request
    participant B as Drawdown request
    participant DB as PostgreSQL
    participant E as Margin evaluator
    participant O as Outbox publisher
    participant K as Kafka
    A->>DB: Lock facility; validate after release
    B->>DB: Lock same facility; wait
    A->>DB: Update collateral + custody; commit
    B->>DB: Re-read state; LTV check fails or succeeds within limit
    B->>DB: Commit result + idempotency response
    E->>DB: Evaluate closed minute; status-gated update + evidence + outbox
    E->>DB: Commit
    O->>DB: Claim pending outbox item
    O->>K: Publish keyed by FacilityId with EventId
    Note over O,K: If Kafka accepts and the process crashes before marking published, the event may be sent again.
    O->>DB: Mark published after broker acknowledgement
```