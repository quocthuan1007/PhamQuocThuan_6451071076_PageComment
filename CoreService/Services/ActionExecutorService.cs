using Confluent.Kafka;
using CoreService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedDomain;
using System.Text.Json;

namespace CoreService.Services;

public interface IActionExecutorService
{
    Task ExecuteActionAsync(
        ActionDecision decision,
        NormalizedEvent ev,
        IntentType intent,
        SentimentType sentiment,
        CancellationToken cancellationToken = default);
}

public class ActionExecutorService : IActionExecutorService, IDisposable
{
    private readonly ILogger<ActionExecutorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IReplyGenerationService _replyGenerationService;
    private readonly IProducer<Null, string> _producer;

    public ActionExecutorService(
        ILogger<ActionExecutorService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IReplyGenerationService replyGenerationService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _replyGenerationService = replyGenerationService;
        _producer = new ProducerBuilder<Null, string>(new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092"
        }).Build();
    }

    public async Task ExecuteActionAsync(
        ActionDecision decision,
        NormalizedEvent ev,
        IntentType intent,
        SentimentType sentiment,
        CancellationToken cancellationToken = default)
    {
        switch (decision)
        {
            case ActionDecision.AutoReply:
            case ActionDecision.ThankUser:
            case ActionDecision.ApologizeUser:
            case ActionDecision.HideComment:
            case ActionDecision.SendToManualReview:
                await PublishFacebookCommandAsync(decision, ev, intent, sentiment, cancellationToken);
                break;

            case ActionDecision.AddToBlacklist:
            case ActionDecision.BlockUser:
                await AddToInternalBlacklistAsync(ev.SenderId, cancellationToken);
                break;

            case ActionDecision.None:
            default:
                _logger.LogInformation("[Automation] No Facebook command for event {EventId}.", ev.EventId);
                break;
        }
    }

    private async Task PublishFacebookCommandAsync(
        ActionDecision decision,
        NormalizedEvent ev,
        IntentType intent,
        SentimentType sentiment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ev.PlatformEventId))
        {
            _logger.LogWarning("Cannot publish Facebook command because PlatformEventId is missing for event {EventId}.", ev.EventId);
            return;
        }

        var replyMessage = string.Empty;
        if (decision is ActionDecision.AutoReply or ActionDecision.ThankUser or ActionDecision.ApologizeUser)
        {
            replyMessage = await _replyGenerationService.GenerateReplyAsync(ev, decision, intent, sentiment, cancellationToken);
        }

        var command = new FacebookCommandMessage
        {
            CommandId = $"{decision}:{ev.PlatformEventId}:{ev.EventId}",
            EventId = ev.EventId,
            PlatformEventId = ev.PlatformEventId,
            SenderId = ev.SenderId,
            Decision = decision,
            ReplyMessage = replyMessage
        };

        await _producer.ProduceAsync(
            Constants.KafkaTopicReplyCommands,
            new Message<Null, string> { Value = JsonSerializer.Serialize(command) },
            cancellationToken);

        _logger.LogInformation(
            "[Kafka] Published command {CommandId} to {Topic} with decision {Decision}",
            command.CommandId,
            Constants.KafkaTopicReplyCommands,
            decision);
    }

    private async Task AddToInternalBlacklistAsync(string senderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(senderId))
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await dbContext.UserBlacklists.AnyAsync(u => u.SenderId == senderId, cancellationToken))
        {
            return;
        }

        dbContext.UserBlacklists.Add(new UserBlacklist { SenderId = senderId, Reason = "Repeated spam" });
        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[Internal] Added user {SenderId} to blacklist.", senderId);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
