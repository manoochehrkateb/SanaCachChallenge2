# Scale And Security Notes

## Scale

- Evaluate active facilities with set-based SQL and indexes aligned to status and facility key; do not issue a query per facility.
- A 30,000-event burst requires bounded outbox batches, per-facility Kafka keys, retries with backoff, and backlog/lag monitoring. Downstream back-pressure must not lose committed outbox rows.
- A one-minute continuous aggregate must expose the closed bucket within the five-second grace period. Verify refresh behavior on the pinned TimescaleDB version.
- The proposed one-day raw-tick chunk, compression, 30-day raw retention, and two-year candle retention are starting assumptions, not benchmarked settings.
- Bound evaluator batch size and use a separate/limited connection pool so API traffic retains capacity.
- Track tick ingest/rejection, feed age, evaluator duration/lag, facility counts by status, transition counts, LTV rejections, idempotency conflicts, outbox backlog, and Kafka consumer lag.

## Security

- Derive client identity from a validated JWT claim. Enforce ownership, LTV, and idempotency inside the service; gateway checks are defense in depth.
- APISIX can apply per-client rate limits, token pre-validation, request-size limits, and partner IP allow-lists. These controls do not replace service authorization or domain invariants.
- Prevent burst abuse with rate limits and durable idempotency. Prevent replay effects through persisted key/fingerprint/result records. Avoid facility enumeration by returning not-found for non-owned identifiers.
- Treat client IDs, debt, and collateral as sensitive. Avoid logging raw payloads or secrets; restrict event payload access and retention.