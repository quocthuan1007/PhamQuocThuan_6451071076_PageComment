using Confluent.Kafka;
using CoreService.Data;
using CoreService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedDomain;
using System.Text.Json;

namespace CoreService.Workers;

public class KafkaConsumerWorker : BackgroundService
{
    private readonly ILogger<KafkaConsumerWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public KafkaConsumerWorker(ILogger<KafkaConsumerWorker> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "core-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        using var producer = new ProducerBuilder<Null, string>(producerConfig).Build();

        consumer.Subscribe(Constants.KafkaTopicRawEvents);
        _logger.LogInformation(
            "Kafka Consumer started listening to '{RawTopic}'",
            Constants.KafkaTopicRawEvents);

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

                    var normalizedEvent = ParseIncomingMessage(consumeResult);
                    if (normalizedEvent is null)
                    {
                        consumer.Commit(consumeResult);
                        continue;
                    }

                    var handled = await ProcessEventPipelineAsync(normalizedEvent, stoppingToken);
                    if (!handled)
                    {
                        await PublishToDeadLetterAsync(
                            producer,
                            normalizedEvent,
                            "Pipeline failed before action completed.",
                            stoppingToken);
                    }

                    consumer.Commit(consumeResult);
                }
                catch (ConsumeException e)
                {
                    _logger.LogError("Consume error: {Reason}", e.Error.Reason);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unexpected consumer loop error");
                }
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
            _logger.LogInformation("Kafka Consumer stopping.");
        }
    }

    private NormalizedEvent? ParseIncomingMessage(ConsumeResult<Ignore, string> consumeResult)
    {
        try
        {
            return JsonSerializer.Deserialize<NormalizedEvent>(consumeResult.Message.Value);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid message format on topic {Topic}", consumeResult.Topic);
            return null;
        }
    }

    private async Task<bool> ProcessEventPipelineAsync(NormalizedEvent ev, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var spamDetector = scope.ServiceProvider.GetRequiredService<ISpamDetectionService>();
        var aiClassifier = scope.ServiceProvider.GetRequiredService<IAiClassificationService>();
        var decisionMaker = scope.ServiceProvider.GetRequiredService<IDecisionMakerService>();
        var actionExecutor = scope.ServiceProvider.GetRequiredService<IActionExecutorService>();

        var existingState = await dbContext.ProcessStates
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(x => x.EventId == ev.EventId, cancellationToken);

        if (existingState is not null && existingState.Status != EventStatus.Failed)
        {
            _logger.LogWarning("[Idempotent] Skip duplicated event {EventId}", ev.EventId);
            return true;
        }

        _logger.LogInformation("[Pipeline Start] Processing event {EventId} from {SenderId}", ev.EventId, ev.SenderId);

        var state = existingState ?? new ProcessState { EventId = ev.EventId };
        if (existingState is null)
        {
            dbContext.ProcessStates.Add(state);
        }

        state.Status = EventStatus.Received;
        state.Remarks = string.Empty;
        state.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var isSpam = spamDetector.IsSpam(ev.SenderId, ev.Content);
            var spamCount = spamDetector.GetSpamCount(ev.SenderId);
            _logger.LogInformation("[SpamCheck] {EventId} | isSpam: {IsSpam} | Count: {SpamCount}", ev.EventId, isSpam, spamCount);

            var (intent, sentiment) = await aiClassifier.ClassifyAsync(ev.Content);
            _logger.LogInformation("[AI] {EventId} | Intent: {Intent} | Sentiment: {Sentiment}", ev.EventId, intent, sentiment);

            var decision = decisionMaker.MakeDecision(ev, isSpam, spamCount, intent, sentiment);
            _logger.LogInformation("[Decision] {EventId} | Decision: {Decision}", ev.EventId, decision);

            await actionExecutor.ExecuteActionAsync(decision, ev, intent, sentiment, cancellationToken);

            state.Status = decision switch
            {
                ActionDecision.AutoReply => EventStatus.Replied,
                ActionDecision.HideComment => EventStatus.Hidden,
                ActionDecision.AddToBlacklist => EventStatus.Blacklisted,
                ActionDecision.BlockUser => EventStatus.Blocked,
                ActionDecision.SendToManualReview => EventStatus.ManualReview,
                ActionDecision.ThankUser or ActionDecision.ApologizeUser => EventStatus.Replied,
                _ => EventStatus.Processed
            };
            state.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("[Pipeline End] Event {EventId} processed successfully.", ev.EventId);
            return true;
        }
        catch (Exception ex)
        {
            state.Status = EventStatus.Failed;
            state.Remarks = ex.Message;
            state.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "[Pipeline Failed] Event {EventId}", ev.EventId);
            return false;
        }
    }

    private async Task PublishToDeadLetterAsync(
        IProducer<Null, string> producer,
        NormalizedEvent ev,
        string reason,
        CancellationToken cancellationToken)
    {
        var payload = new DeadLetterEventMessage
        {
            Event = ev,
            RetryCount = 0,
            MaxRetryCount = 0,
            FailedAtUtc = DateTime.UtcNow,
            FailureReason = reason,
            SourceTopic = Constants.KafkaTopicRawEvents
        };

        var message = new Message<Null, string>
        {
            Value = JsonSerializer.Serialize(payload)
        };

        await producer.ProduceAsync(Constants.KafkaTopicDeadLetter, message, cancellationToken);
        _logger.LogError(
            "[DLQ] Event {EventId} moved to {Topic}. Reason: {Reason}",
            ev.EventId,
            Constants.KafkaTopicDeadLetter,
            reason);
    }
}
