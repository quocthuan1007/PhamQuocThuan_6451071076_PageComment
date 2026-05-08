using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedDomain;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebhookService.Services;

public interface IKafkaProducerService
{
    Task PublishEventAsync(NormalizedEvent normalizedEvent);
}

public class KafkaProducerService : IKafkaProducerService
{
    private readonly IProducer<Null, string> _producer;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(IConfiguration configuration, ILogger<KafkaProducerService> logger)
    {
        _logger = logger;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092"
        };
        _producer = new ProducerBuilder<Null, string>(producerConfig).Build();
    }

    public async Task PublishEventAsync(NormalizedEvent normalizedEvent)
    {
        var message = new Message<Null, string>
        {
            Value = JsonSerializer.Serialize(normalizedEvent)
        };

        var deliveryResult = await _producer.ProduceAsync(Constants.KafkaTopicRawEvents, message);
        _logger.LogInformation($"Delivered '{deliveryResult.Value}' to '{deliveryResult.TopicPartitionOffset}'");
    }
}
