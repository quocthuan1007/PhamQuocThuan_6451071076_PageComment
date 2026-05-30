namespace BackendApi.Models;

public class ProcessedCommand
{
    public int Id { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string PlatformEventId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
