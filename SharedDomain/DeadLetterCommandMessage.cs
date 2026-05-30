namespace SharedDomain;

public class DeadLetterCommandMessage
{
    public FacebookCommandMessage Command { get; set; } = new();
    public DateTime FailedAtUtc { get; set; } = DateTime.UtcNow;
    public string FailureReason { get; set; } = string.Empty;
    public string SourceTopic { get; set; } = Constants.KafkaTopicSendFailed;
}
