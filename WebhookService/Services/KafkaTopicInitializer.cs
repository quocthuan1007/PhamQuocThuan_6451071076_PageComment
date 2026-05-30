using Confluent.Kafka;
using Confluent.Kafka.Admin;
using SharedDomain;

namespace WebhookService.Services;

public class KafkaTopicInitializer
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaTopicInitializer> _logger;

    public KafkaTopicInitializer(IConfiguration configuration, ILogger<KafkaTopicInitializer> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task EnsureTopicsExistAsync(CancellationToken cancellationToken = default)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers
        }).Build();

        var topics = new[]
        {
            Constants.KafkaTopicRawEvents,
            Constants.KafkaTopicReplyCommands,
            Constants.KafkaTopicSendRetry,
            Constants.KafkaTopicSendFailed,
            Constants.KafkaTopicDeadLetter
        };

        try
        {
            await adminClient.CreateTopicsAsync(
                topics.Select(topic => new TopicSpecification
                {
                    Name = topic,
                    NumPartitions = 1,
                    ReplicationFactor = 1
                }));

            _logger.LogInformation("Ensured Kafka topics exist: {Topics}", string.Join(", ", topics));
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            _logger.LogInformation("Kafka topics already existed: {Topics}", string.Join(", ", topics));
        }
    }
}
