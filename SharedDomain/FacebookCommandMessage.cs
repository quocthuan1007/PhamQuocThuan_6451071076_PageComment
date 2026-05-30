namespace SharedDomain;

public class FacebookCommandMessage
{
    public string CommandId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string PlatformEventId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public ActionDecision Decision { get; set; }
    public string ReplyMessage { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; } = 3;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
