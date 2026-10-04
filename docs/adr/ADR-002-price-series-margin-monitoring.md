# ADR-002: Price Series And Margin Monitoring On TimescaleDB

## Context

Client actions need a fresh latest tick, while risk decisions must use stable closed-minute prices after a five-second late-tick grace period. Silent feeds must stop margin transitions after two minutes, and the evaluator must process many facilities set-wise.

## Decision

Store accepted ticks in a TimescaleDB hypertable keyed logically by instrument and sequence, with timestamp-aware latest-price indexes. Build one-minute OHLC candles as a continuous aggregate with real-time aggregation enabled; evaluate only after the bucket close plus five seconds. Use raw ticks for latest-price lookup and the candle/gap-fill path for closed-minute evaluation. Carry forward the last close for empty minutes, but determine feed staleness from the latest real tick timestamp. Persist minute evaluation evidence and a durable evaluator watermark.

Use versioned SQL migrations for all TimescaleDB objects. Proposed defaults are a one-day chunk interval, 30-day raw-tick retention, and two-year candle retention; validate these against the supported TimescaleDB image before treating them as final.

## Alternatives

- Evaluate directly from live ticks: rejected because it would make decisions arrival-order and grace-period dependent.
- Use raw ticks only for every evaluation: rejected because repeated candle calculation is costly and obscures the closed-minute contract.
- Treat carry-forward values as indefinitely valid: rejected because a silent feed is not proof that the market price is unchanged.
- Depend on a fire-and-forget timer: rejected because it loses progress across restarts.

## Consequences

The aggregate refresh schedule and query path must make the closed bucket visible by the five-second grace deadline. Retention must preserve raw data long enough for aggregate refresh; retention and refresh policies require deployment-level verification. Evaluations are set-based and record the inputs used for each transition.

## Revisit When

Revisit chunk sizing, refresh, and retention after representative workload measurements, a changed feed rate, or a new requirement for historical recomputation. Revisit the candle source if refresh latency cannot meet the grace-period objective.