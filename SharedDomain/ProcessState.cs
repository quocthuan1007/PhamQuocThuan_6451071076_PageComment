using System;

namespace SharedDomain;

public class ProcessState
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public EventStatus Status { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
