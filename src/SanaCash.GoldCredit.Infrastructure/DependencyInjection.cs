using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Confluent.Kafka;
using Npgsql;
using SanaCash.GoldCredit.Application.Abstractions.Authentication;
using SanaCash.GoldCredit.Application.Abstractions.Common;
using SanaCash.GoldCredit.Application.Abstractions.Data;
using SanaCash.GoldCredit.Application.Abstractions.Health;
using SanaCash.GoldCredit.Application.Abstractions.Pricing;
using SanaCash.GoldCredit.Application.Credit.ExecuteDrawdown;
using SanaCash.GoldCredit.Application.Credit.EvaluateClosedMinute;
using SanaCash.GoldCredit.Application.Credit.GetFacility;
using SanaCash.GoldCredit.Application.Credit.GetMarginEvents;
using SanaCash.GoldCredit.Application.Credit.PledgeCollateral;
using SanaCash.GoldCredit.Application.Credit.ReleaseCollateral;
using SanaCash.GoldCredit.Application.Credit.RepayDebt;
using SanaCash.GoldCredit.Application.Pricing.IngestPriceTick;
using SanaCash.GoldCredit.Application.Pricing.GetCandles;
using SanaCash.GoldCredit.Domain.Credit;
using SanaCash.GoldCredit.Domain.Custody;
using SanaCash.GoldCredit.Domain.Pricing;
using SanaCash.GoldCredit.Infrastructure.Authentication;
using SanaCash.GoldCredit.Infrastructure.BackgroundJobs;
using SanaCash.GoldCredit.Infrastructure.Common;
using SanaCash.GoldCredit.Infrastructure.Messaging;
using SanaCash.GoldCredit.Infrastructure.HealthChecks;
using SanaCash.GoldCredit.Persistence.Common;
using SanaCash.GoldCredit.Persistence.Credit;
using SanaCash.GoldCredit.Persistence.Custody;
using SanaCash.GoldCredit.Persistence.Migrations;
using SanaCash.GoldCredit.Persistence.Pricing;

namespace SanaCash.GoldCredit.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGoldCreditInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("GoldCredit")
            ?? throw new InvalidOperationException("ConnectionStrings:GoldCredit is required.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContext<GoldCreditDbContext>(options =>
            options.UseNpgsql(connectionString));
        var kafkaConfiguration = configuration.GetSection("Kafka");
        services.Configure<KafkaOptions>(options =>
        {
            options.BootstrapServers = kafkaConfiguration["BootstrapServers"] ?? options.BootstrapServers;
            options.ReferencePricesTopic = kafkaConfiguration["ReferencePricesTopic"] ?? options.ReferencePricesTopic;
            options.FacilityEventsTopic = kafkaConfiguration["FacilityEventsTopic"] ?? options.FacilityEventsTopic;
            options.ConsumerGroupId = kafkaConfiguration["ConsumerGroupId"] ?? options.ConsumerGroupId;
        });
        services.AddSingleton<IProducer<string, string>>(serviceProvider =>
        {
            var kafka = serviceProvider.GetRequiredService<IOptions<KafkaOptions>>().Value;
            return new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                EnableIdempotence = true
            }).Build();
        });
        services.AddSingleton<OutboxPublisher>();
        services.AddSingleton<MigrationRunner>();
        services.AddScoped<PostgresSession>();
        services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<ICreditFacilityRepository, PostgresCreditFacilityRepository>();
        services.AddScoped<IFacilityReadStore, PostgresFacilityReadStore>();
        services.AddScoped<IMarginEvaluationRepository, PostgresMarginEvaluationRepository>();
        services.AddScoped<ICustodyHoldingRepository, PostgresCustodyHoldingRepository>();
        services.AddScoped<IPriceTickRepository, PostgresPriceTickRepository>();
        services.AddScoped<IMarketReadStore, PostgresMarketReadStore>();
        services.AddScoped<IRejectedPriceTickRepository, PostgresRejectedPriceTickRepository>();
        services.AddScoped<IReferencePriceProvider, PostgresReferencePriceProvider>();
        services.AddScoped<IIdempotencyStore, PostgresIdempotencyStore>();
        services.AddScoped<IOutboxWriter, PostgresOutboxWriter>();
        services.AddScoped<IPlatformHealthProbe, PlatformHealthProbe>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ICurrentClient, HttpCurrentClient>();
        services.AddSingleton<PriceTickValidator>();
        services.AddSingleton<PriceFreshnessPolicy>();
        services.AddSingleton<FeedStalenessPolicy>();
        services.AddSingleton<MarginPolicy>();
        services.AddSingleton<CollateralTransferService>();
        services.AddScoped<IngestPriceTickHandler>();
        services.AddScoped<PledgeCollateralHandler>();
        services.AddScoped<ReleaseCollateralHandler>();
        services.AddScoped<ExecuteDrawdownHandler>();
        services.AddScoped<RepayDebtHandler>();
        services.AddScoped<GetFacilityHandler>();
        services.AddScoped<GetMarginEventsHandler>();
        services.AddScoped<GetCandlesHandler>();
        services.AddScoped<EvaluateClosedMinuteHandler>();
        services.AddHostedService<ReferencePriceConsumerJob>();
        services.AddHostedService<OutboxPublisherJob>();
        services.AddHostedService<MarginEvaluatorJob>();
        return services;
    }
}
