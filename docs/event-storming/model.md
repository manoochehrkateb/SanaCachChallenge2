# Focused Event Storming

## Actors And External Systems

- Client treasury system: pledge, release, drawdown, and repay.
- Risk evaluator: evaluates eligible closed minutes.
- Market price feed and Kafka: publish reference ticks.
- PostgreSQL/TimescaleDB: durable state, price series, evaluation evidence, idempotency, and outbox.
- Downstream dealing desk: consumes facility integration events; liquidation execution is out of scope.

## Commands And Domain Events

| Command | Successful event | Expected failure |
| --- | --- | --- |
| Ingest price tick | PriceTickReceived | PriceTickRejected with a reason |
| Pledge collateral | CollateralPledged | PledgeRejected result |
| Release collateral | CollateralReleased | ReleaseRejected result |
| Execute drawdown | DrawdownExecuted | DrawdownRejected result |
| Repay debt | RepaymentReceived | RepaymentRejected result |
| Evaluate closed minute | MarginCallIssued, MarginCallCured, or LiquidationRequired | No transition when evidence does not meet policy |
| Close price minute | MinuteClosed | Feed-stale pause when latest real tick is too old |

## Policies And Models

- Price tick validation and freshness policy.
- Closed-minute creation after the grace period; gap-fill with prior close while the feed remains live.
- Margin policy: liquidation above 8,000 bps; margin call after three consecutive minutes above 7,000 bps; cure at or below 6,500 bps.
- Aggregates: CreditFacility owns debt, pledged collateral, and status; CustodyHolding owns free gold.
- Read models: facility view, candle series, margin-event history, feed status.

## Hotspots

- Drawdown and release share the facility aggregate to prevent LTV write skew.
- Pledge crosses custody and credit roots and must commit in one database transaction.
- Client commands use the latest tick; evaluator transitions use grace-period closed minutes.
- A breach episode begins when a Healthy facility transitions to MarginCall; status-gated writes and database uniqueness prevent duplicate transition events.
- After downtime, skip stale historical decisions and evaluate the latest eligible closed minute; record the gap.
- A silent feed is distinct from an unchanged price: pause transitions after two minutes without a real tick.
- Domain events describe internal decisions; selected state changes become versioned integration events through the outbox.