using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedDomain;
using System.Text.Json;

namespace RetryService.Workers;

public class RetryWorker : BackgroundService
{
    private readonly ILogger<RetryWorker> _logger;
    private readonly IConfiguration _configuration;

    public RetryWorker(ILogger<RetryWorker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "retry-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();

        consumer.Subscribe(Constants.KafkaTopicSendFailed);
        _logger.LogInformation("RetryService started listening to '{Topic}'", Constants.KafkaTopicSendFailed);

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

                    var failedMessage = JsonSerializer.Deserialize<FailedCommandMessage>(consumeResult.Message.Value);
                    if (failedMessage is null || string.IsNullOrWhiteSpace(failedMessage.Command.CommandId))
                    {
                        _logger.LogWarning("Skipping invalid failed command message from {Topic}", Constants.KafkaTopicSendFailed);
                        consumer.Commit(consumeResult);
                        continue;
                    }

                    await HandleFailedMessageAsync(producer, failedMessage, stoppingToken);
                    consumer.Commit(consumeResult);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "RetryService consume error: {Reason}", ex.Error.Reason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RetryService loop error");
                }
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
            _logger.LogInformation("RetryService stopping.");
        }
    }

    private async Task HandleFailedMessageAsync(
        IProducer<Null, string> producer,
        FailedCommandMessage failedMessage,
        CancellationToken cancellationToken)
    {
        if (failedMessage.Command.RetryCount >= failedMessage.Command.MaxRetryCount)
        {
            await PublishDeadLetterAsync(producer, failedMessage, cancellationToken);
            return;
        }

        var nextRetryCount = failedMessage.Command.RetryCount + 1;
        var delay = TimeSpan.FromSeconds(Math.Pow(2, failedMessage.Command.RetryCount));
        _logger.LogWarning(
            "Scheduling retry {RetryCount}/{MaxRetryCount} for command {CommandId} after {DelaySeconds}s",
            nextRetryCount,
            failedMessage.Command.MaxRetryCount,
            failedMessage.Command.CommandId,
            delay.TotalSeconds);

        await Task.Delay(delay, cancellationToken);

        var retryCommand = failedMessage.Command;
        retryCommand.RetryCount = nextRetryCount;

        await producer.ProduceAsync(
            Constants.KafkaTopicSendRetry,
            new Message<Null, string> { Value = JsonSerializer.Serialize(retryCommand) },
            cancellationToken);

        _logger.LogInformation(
            "Published retry {RetryCount}/{MaxRetryCount} for command {CommandId} to {Topic}",
            nextRetryCount,
            retryCommand.MaxRetryCount,
            retryCommand.CommandId,
            Constants.KafkaTopicSendRetry);
    }

    private async Task PublishDeadLetterAsync(
        IProducer<Null, string> producer,
        FailedCommandMessage failedMessage,
        CancellationToken cancellationToken)
    {
        var deadLetterMessage = new DeadLetterCommandMessage
        {
            Command = failedMessage.Command,
            FailedAtUtc = failedMessage.FailedAtUtc,
            FailureReason = failedMessage.FailureReason,
            SourceTopic = Constants.KafkaTopicSendFailed
        };

        await producer.ProduceAsync(
            Constants.KafkaTopicDeadLetter,
            new Message<Null, string> { Value = JsonSerializer.Serialize(deadLetterMessage) },
            cancellationToken);

        _logger.LogError(
            "[DLQ] Command {CommandId} moved to {Topic} after {RetryCount} retries. Reason: {Reason}",
            failedMessage.Command.CommandId,
            Constants.KafkaTopicDeadLetter,
            failedMessage.Command.RetryCount,
            failedMessage.FailureReason);
    }
}
