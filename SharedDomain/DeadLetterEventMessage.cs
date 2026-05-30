namespace SharedDomain;

public class DeadLetterEventMessage
{
    public NormalizedEvent Event { get; set; } = new();
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; }
    public DateTime FailedAtUtc { get; set; } = DateTime.UtcNow;
    public string FailureReason { get; set; } = string.Empty;
    public string SourceTopic { get; set; } = Constants.KafkaTopicSendFailed;
}
