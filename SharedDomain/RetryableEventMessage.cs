namespace SharedDomain;

public class RetryableEventMessage
{
    public NormalizedEvent Event { get; set; } = new();
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; } = 3;
    public DateTime FirstFailedAtUtc { get; set; } = DateTime.UtcNow;
    public string LastFailureReason { get; set; } = string.Empty;
}
