# ADR-001: Credit Facility, Collateral, And Custody

## Context

Drawdown and collateral release both change facility LTV. Checking debt and collateral in separate transactions can create write skew. Pledging also moves gold from a client-wide free holding into one facility, so concurrent pledges across facilities must not spend the same gold twice.

## Decision

`CreditFacility` is the aggregate root for pledged collateral, outstanding debt, and status. `CustodyHolding` is a separate aggregate root keyed by client and instrument. A pledge or release coordinates both roots in one database transaction, with a consistent row-lock order and database constraints as the durable concurrency guard. The domain transfer service performs cross-context rule checks; application orchestration owns the transaction.

The aggregate root is an entity with a consistency boundary; it is not a separate DDD building block from entities. The folders distinguish roots from other entities for readability.

## Alternatives

- Separate debt and collateral aggregates: rejected because concurrent updates could each pass their local LTV check and jointly violate the facility limit.
- Put all custody holdings inside each facility: rejected because the same client holding spans multiple facilities.
- Rely on a process or distributed lock: rejected because it is not the durable invariant boundary.

## Consequences

Facility mutations serialize on one facility row; custody transfers additionally serialize on the client holding. The transaction must include both updates and any idempotency/outbox records. Cross-facility custody contention may reduce throughput but protects the free-gold invariant.

## Revisit When

Revisit if collateral becomes independently shared across facilities, if custody is moved behind a separate service, or if measured contention requires a new allocation model with equivalent database-enforced guarantees.