# SanaCash GoldCredit Challenge

This is a .NET 10 modular-monolith starter for the gold-collateralized credit challenge. The solution is intentionally separate from the original `SanaCash.sln`.

## Structure

- `SanaCash.GoldCredit.Domain` owns pricing, custody, and credit rules. Aggregate roots are `CustodyHolding` and `CreditFacility`; `FacilityStatus` and the other enums are grouped under their bounded-context folders.
- `SanaCash.GoldCredit.Application` owns use cases, orchestration, and ports.
- `SanaCash.GoldCredit.Persistence` implements durable storage and versioned migrations.
- `SanaCash.GoldCredit.Infrastructure` wires external adapters and hosted workers.
- `SanaCash.GoldCredit.Presentation` is the ASP.NET Core composition root and API host.
- `tests` separates Domain, Application, Infrastructure, Presentation, and database integration tests.
- `tools/paths` contains scripted price paths for the simulator.

## Build And Test

From this directory:

```powershell
dotnet build .\SanaCash.GoldCredit.sln
dotnet test .\tests\SanaCash.GoldCredit.Domain.UnitTests\SanaCash.GoldCredit.Domain.UnitTests.csproj
```

The Domain suite covers integer valuation, worked rounding examples, aggregate invariants, margin thresholds, tick validation, and feed freshness. The Application suite covers drawdown, pledge, release, repayment, ownership, and stale-price behavior.

For the live PostgreSQL/TimescaleDB integration suite, start the database and set the test connection:

```powershell
docker compose up -d postgres
$env:SANACASH_TEST_CONNECTION = "Host=localhost;Port=5433;Database=goldcredit;Username=goldcredit;Password=goldcredit"
dotnet test .\tests\SanaCash.GoldCredit.IntegrationTests\SanaCash.GoldCredit.IntegrationTests.csproj
```

The integration suite applies the migrations and covers duplicate/out-of-order ticks, malformed-tick persistence, 20 concurrent drawdowns, shared-custody pledge contention, 50 release-vs-drawdown races, and competing margin evaluators.

## Infrastructure

```powershell
docker compose up -d
```

PostgreSQL/TimescaleDB is exposed on port `5433`; Kafka is exposed on `9092`. Local development credentials are defined in Compose and must not be reused outside the assessment environment.

## Price Simulator

Replay a scripted path after Kafka is running:

```powershell
dotnet run --project .\tools\PriceSimulator\SanaCash.GoldCredit.PriceSimulator.csproj -- .\tools\paths\margin-call.csv
```

Other starter paths are `cure.csv`, `liquidation.csv`, and `feed-outage.csv` under `tools/paths`.

## Known Gaps

- Kafka ingestion, outbox publication, and simulator publishing compile, but the end-to-end broker path and publish-before-mark restart scenario have not been exercised. The current Compose run has PostgreSQL healthy; Kafka still needs a live smoke test.
- Durable same-key idempotency replay and key/payload conflict handling are implemented in Persistence, but their concurrent duplicate/restart scenarios do not yet have dedicated real-database tests.
- Database integration tests currently prove the high-contention drawdown, shared-custody pledge, release/drawdown write-skew, tick deduplication, and evaluator races; broader API authorization and outbox recovery tests remain.
- `GET /health` and read routes build, but no live HTTP/JWT request test has been run. Infrastructure and Presentation unit-test projects still contain no test cases.
- Structured logs and baseline worker logs exist, but the complete requested metrics, trace propagation, and sensitive-data review are unfinished.
- ADRs and diagrams document the intended architecture. Migration scripts were applied successfully to the local TimescaleDB image, but no representative scale benchmark or query-plan review has been performed.
- Optional LTV exposure history is not implemented.