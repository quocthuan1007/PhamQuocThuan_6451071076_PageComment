using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedDomain;
using CoreService.Services;
using CoreService.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = "core-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false // Manual commit for resilience
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        consumer.Subscribe(Constants.KafkaTopicRawEvents);

        _logger.LogInformation("Kafka Consumer started listening to 'raw_events'");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(stoppingToken);

                    if (consumeResult != null)
                    {
                        var eventJson = consumeResult.Message.Value;
                        var normalizedEvent = JsonSerializer.Deserialize<NormalizedEvent>(eventJson);

                        if (normalizedEvent != null)
                        {
                            await ProcessEventPipelineAsync(normalizedEvent);
                        }

                        // Commit offset only after successful processing
                        consumer.Commit(consumeResult);
                    }
                }
                catch (ConsumeException e)
                {
                    _logger.LogError($"Error occured: {e.Error.Reason}");
                }
                catch (Exception e)
                {
                    _logger.LogError($"Unexpected error processing message: {e.Message}");
                    // Here we could implement Dead Letter Queue (publish failed event to dead_letter_events)
                }
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
            _logger.LogInformation("Kafka Consumer stopping.");
        }
    }

    private async Task ProcessEventPipelineAsync(NormalizedEvent ev)
    {
        using var scope = _serviceProvider.CreateScope();
        
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var spamDetector = scope.ServiceProvider.GetRequiredService<ISpamDetectionService>();
        var aiClassifier = scope.ServiceProvider.GetRequiredService<IAiClassificationService>();
        var decisionMaker = scope.ServiceProvider.GetRequiredService<IDecisionMakerService>();
        var actionExecutor = scope.ServiceProvider.GetRequiredService<IActionExecutorService>();

        _logger.LogInformation($"[Pipeline Start] Processing event {ev.EventId} from {ev.SenderId}");

        // 1. Initial State Tracking
        var state = new ProcessState { EventId = ev.EventId, Status = EventStatus.Received };
        dbContext.ProcessStates.Add(state);
        await dbContext.SaveChangesAsync();

        // 2. Spam Detection
        bool isSpam = spamDetector.IsSpam(ev.SenderId, ev.Content);
        int spamCount = spamDetector.GetSpamCount(ev.SenderId);
        _logger.LogInformation($"[SpamCheck] {ev.EventId} | isSpam: {isSpam} | Count: {spamCount}");

        // 3. AI Classification
        var (intent, sentiment) = await aiClassifier.ClassifyAsync(ev.Content);
        _logger.LogInformation($"[AI] {ev.EventId} | Intent: {intent} | Sentiment: {sentiment}");

        // 4. Decision Making
        var decision = decisionMaker.MakeDecision(ev, isSpam, spamCount, intent, sentiment);
        _logger.LogInformation($"[Decision] {ev.EventId} | Decision: {decision}");

        // 5. Action Execution
        await actionExecutor.ExecuteActionAsync(decision, ev);

        // 6. Update State Tracking
        state.Status = decision switch
        {
            ActionDecision.HideComment => EventStatus.Hidden,
            ActionDecision.AddToBlacklist => EventStatus.Blacklisted,
            ActionDecision.BlockUser => EventStatus.Blocked,
            ActionDecision.SendToManualReview => EventStatus.ManualReview,
            ActionDecision.None => EventStatus.Processed,
            _ => EventStatus.Processed
        };
        state.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        _logger.LogInformation($"[Pipeline End] Event {ev.EventId} processed successfully.");
    }
}
