# Price Simulator

Replay a CSV path to `market.reference-prices.v1`. CSV columns are `timestamp,price`; timestamps must increase and prices must be positive integers. The gaps between timestamps determine playback delays. Timestamps in emitted messages use the current UTC time so the receiving service sees fresh data.

```powershell
dotnet run --project .\tools\PriceSimulator\SanaCash.GoldCredit.PriceSimulator.csproj -- .\tools\paths\margin-call.csv
```

Set `KAFKA_BOOTSTRAP_SERVERS` or `KAFKA_PRICE_TOPIC` to override the defaults. Requires a Kafka broker at `localhost:9092` unless configured otherwise.