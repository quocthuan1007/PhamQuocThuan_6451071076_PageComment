namespace SharedDomain;

public class FailedEventMessage
{
    public NormalizedEvent Event { get; set; } = new();
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; } = 3;
    public DateTime FailedAtUtc { get; set; } = DateTime.UtcNow;
    public string FailureReason { get; set; } = string.Empty;
}
