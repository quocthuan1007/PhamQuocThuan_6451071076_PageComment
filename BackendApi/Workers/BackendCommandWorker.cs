using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Services;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using SharedDomain;
using System.Text.Json;

namespace BackendApi.Workers;

public class BackendCommandWorker : BackgroundService
{
    private readonly ILogger<BackendCommandWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFacebookGraphApiService _facebookGraphApiService;

    public BackendCommandWorker(
        ILogger<BackendCommandWorker> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IFacebookGraphApiService facebookGraphApiService)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _facebookGraphApiService = facebookGraphApiService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "backend-api-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();

        consumer.Subscribe(new[]
        {
            Constants.KafkaTopicReplyCommands,
            Constants.KafkaTopicSendRetry
        });

        _logger.LogInformation(
            "BackendApi started listening to '{ReplyTopic}' and '{RetryTopic}'",
            Constants.KafkaTopicReplyCommands,
            Constants.KafkaTopicSendRetry);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(stoppingToken);
                    if (consumeResult?.Message?.Value is null)
                    {
                        continue;
                    }

                    var command = JsonSerializer.Deserialize<FacebookCommandMessage>(consumeResult.Message.Value);
                    if (command is null || string.IsNullOrWhiteSpace(command.CommandId))
                    {
                        _logger.LogWarning("Skipping invalid Facebook command from topic {Topic}", consumeResult.Topic);
                        consumer.Commit(consumeResult);
                        continue;
                    }

                    await HandleCommandAsync(producer, command, stoppingToken);
                    consumer.Commit(consumeResult);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "BackendApi consume error: {Reason}", ex.Error.Reason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "BackendApi loop error");
                }
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
            _logger.LogInformation("BackendApi consumer stopping.");
        }
    }

    private async Task HandleCommandAsync(
        IProducer<Null, string> producer,
        FacebookCommandMessage command,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackendDbContext>();

        var alreadyProcessed = await dbContext.ProcessedCommands
            .AnyAsync(x => x.CommandId == command.CommandId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogWarning("[Idempotent] Skip duplicated command {CommandId}", command.CommandId);
            return;
        }

        try
        {
            await _facebookGraphApiService.SendCommandAsync(command, cancellationToken);

            dbContext.ProcessedCommands.Add(new ProcessedCommand
            {
                CommandId = command.CommandId,
                EventId = command.EventId,
                PlatformEventId = command.PlatformEventId,
                ProcessedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[Command Done] {CommandId} processed successfully.", command.CommandId);
        }
        catch (Exception ex)
        {
            await PublishSendFailedAsync(producer, command, ex.Message, cancellationToken);
            _logger.LogError(ex, "[Command Failed] {CommandId} moved to {Topic}", command.CommandId, Constants.KafkaTopicSendFailed);
        }
    }

    private static async Task PublishSendFailedAsync(
        IProducer<Null, string> producer,
        FacebookCommandMessage command,
        string reason,
        CancellationToken cancellationToken)
    {
        var failedMessage = new FailedCommandMessage
        {
            Command = command,
            FailedAtUtc = DateTime.UtcNow,
            FailureReason = reason
        };

        await producer.ProduceAsync(
            Constants.KafkaTopicSendFailed,
            new Message<Null, string> { Value = JsonSerializer.Serialize(failedMessage) },
            cancellationToken);
    }
}
