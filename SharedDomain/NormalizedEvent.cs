using System;

namespace SharedDomain;

public class NormalizedEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString(); // Internal ID
    public string PlatformEventId { get; set; } = string.Empty; // FB Comment ID
    public string Source { get; set; } = "Facebook"; // Page Comment, Messenger, etc.
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string RawPayload { get; set; } = string.Empty; // Store original payload for debugging
}
