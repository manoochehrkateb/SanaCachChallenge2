# Context And Aggregate View

```mermaid
flowchart LR
    Market[Market price feed] --> Pricing[Pricing context]
    Pricing -->|Reference price port| Credit[Credit context]
    Client[Client] --> Credit
    Credit -->|one transaction for pledge/release| Custody[Custody context]
    Credit --> DB[(PostgreSQL + TimescaleDB)]
    Pricing --> DB
    Credit --> Outbox[Transactional outbox]
    Outbox --> Kafka[Kafka facility-events topic]
    Kafka --> Desk[Downstream dealing desk]
    Credit --- FacilityAR[CreditFacility aggregate root]
    Custody --- HoldingAR[CustodyHolding aggregate root]
    Pricing --- Tick[Immutable PriceTick entity]
```

Project dependency direction: Domain has no infrastructure dependency; Application depends on Domain; Persistence implements application ports; Infrastructure composes adapters and background jobs; Presentation is the single web composition root.