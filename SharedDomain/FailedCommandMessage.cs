namespace SharedDomain;

public class FailedCommandMessage
{
    public FacebookCommandMessage Command { get; set; } = new();
    public DateTime FailedAtUtc { get; set; } = DateTime.UtcNow;
    public string FailureReason { get; set; } = string.Empty;
}
