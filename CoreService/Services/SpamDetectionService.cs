using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace CoreService.Services;

public interface ISpamDetectionService
{
    bool IsSpam(string senderId, string content);
    int GetSpamCount(string senderId);
}

public class SpamDetectionService : ISpamDetectionService
{
    private readonly IMemoryCache _cache;
    private static readonly Regex LinkRegex = new(@"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SpamDetectionService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool IsSpam(string senderId, string content)
    {
        // 1. Check for links
        if (LinkRegex.IsMatch(content))
        {
            IncrementSpamCount(senderId);
            return true;
        }

        // 2. Check for duplicate content (simplified)
        var cacheKey = $"last_msg_{senderId}";
        if (_cache.TryGetValue(cacheKey, out string? lastMessage))
        {
            if (lastMessage == content)
            {
                IncrementSpamCount(senderId);
                return true;
            }
        }

        _cache.Set(cacheKey, content, TimeSpan.FromMinutes(10));
        return false;
    }

    public int GetSpamCount(string senderId)
    {
        var spamCountKey = $"spam_count_{senderId}";
        return _cache.TryGetValue(spamCountKey, out int count) ? count : 0;
    }

    private void IncrementSpamCount(string senderId)
    {
        var spamCountKey = $"spam_count_{senderId}";
        var count = GetSpamCount(senderId);
        _cache.Set(spamCountKey, count + 1, TimeSpan.FromHours(24));
    }
}
