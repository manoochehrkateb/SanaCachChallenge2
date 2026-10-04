using System.Globalization;
using System.Text.Json;
using Confluent.Kafka;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SanaCash.GoldCredit.PriceSimulator <path.csv>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
if (!File.Exists(path))
{
    Console.Error.WriteLine($"CSV file not found: {path}");
    return 2;
}

var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
var topic = Environment.GetEnvironmentVariable("KAFKA_PRICE_TOPIC") ?? "market.reference-prices.v1";
var sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000;
var ticks = new List<(DateTimeOffset Timestamp, long Price)>();

foreach (var line in File.ReadLines(path))
{
    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("timestamp", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var columns = line.Split(',');
    if (columns.Length != 2 ||
        !DateTimeOffset.TryParse(columns[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) ||
        !long.TryParse(columns[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var price) ||
        price <= 0)
    {
        Console.Error.WriteLine($"Invalid CSV row: {line}");
        return 1;
    }

    if (ticks.Count > 0 && timestamp <= ticks[^1].Timestamp)
    {
        Console.Error.WriteLine("CSV timestamps must be strictly increasing.");
        return 1;
    }

    ticks.Add((timestamp.ToUniversalTime(), price));
}

if (ticks.Count == 0)
{
    Console.Error.WriteLine("CSV contains no price ticks.");
    return 1;
}

using var producer = new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = bootstrapServers,
    Acks = Acks.All,
    EnableIdempotence = true
}).Build();

var sourceStart = ticks[0].Timestamp;
var playbackStart = DateTimeOffset.UtcNow.AddSeconds(1);
var sent = 0;
foreach (var tick in ticks)
{
    var scheduledAt = playbackStart + (tick.Timestamp - sourceStart);
    var delay = scheduledAt - DateTimeOffset.UtcNow;
    if (delay > TimeSpan.Zero)
    {
        await Task.Delay(delay);
    }

    var currentSequence = Interlocked.Increment(ref sequence);
    var publishedAt = DateTimeOffset.UtcNow;
    var payload = JsonSerializer.Serialize(new
    {
        instrument = "XAU-750",
        sequence = currentSequence,
        price = tick.Price,
        timestamp = publishedAt
    });

    await producer.ProduceAsync(topic, new Message<string, string>
    {
        Key = "XAU-750",
        Value = payload,
        Timestamp = new Timestamp(publishedAt.UtcDateTime)
    });

    sent++;
    Console.WriteLine($"Published sequence {currentSequence} at {publishedAt:O}");
}

producer.Flush(TimeSpan.FromSeconds(10));
Console.WriteLine($"Published {sent} price ticks to {topic}.");
return 0;