using System;

namespace SharedDomain;

public class UserBlacklist
{
    public int Id { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
}
