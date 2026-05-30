using System.Net;
using System.Text.Json;

namespace CoreService.Services;

public class GeminiApiStateService
{
    private readonly object _lock = new();
    private DateTime _disabledUntilUtc = DateTime.MinValue;

    public bool IsAvailable()
    {
        lock (_lock)
        {
            return _disabledUntilUtc <= DateTime.UtcNow;
        }
    }

    public TimeSpan GetRemainingCooldown()
    {
        lock (_lock)
        {
            if (_disabledUntilUtc <= DateTime.UtcNow)
            {
                return TimeSpan.Zero;
            }

            return _disabledUntilUtc - DateTime.UtcNow;
        }
    }

    public void DisableFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        lock (_lock)
        {
            var nextDisabledUntil = DateTime.UtcNow.Add(duration);
            if (nextDisabledUntil > _disabledUntilUtc)
            {
                _disabledUntilUtc = nextDisabledUntil;
            }
        }
    }

    public static bool ShouldDisable(HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode == 429)
        {
            return body.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
                || body.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
                || body.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || body.Contains("resource_exhausted", StringComparison.OrdinalIgnoreCase);
        }

        if ((int)statusCode == 403)
        {
            return body.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase)
                || body.Contains("denied access", StringComparison.OrdinalIgnoreCase);
        }

        if ((int)statusCode == 400)
        {
            return body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
                || body.Contains("api key expired", StringComparison.OrdinalIgnoreCase)
                || body.Contains("please renew the api key", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static TimeSpan GetSuggestedCooldown(HttpStatusCode statusCode, string body)
    {
        var retryDelay = TryParseRetryDelay(body);
        if (retryDelay > TimeSpan.Zero)
        {
            return retryDelay.Value + TimeSpan.FromSeconds(2);
        }

        return (int)statusCode switch
        {
            429 => TimeSpan.FromMinutes(10),
            403 => TimeSpan.FromHours(1),
            400 => TimeSpan.FromHours(1),
            _ => TimeSpan.Zero
        };
    }

    private static TimeSpan? TryParseRetryDelay(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var errorElement))
            {
                return null;
            }

            if (!errorElement.TryGetProperty("details", out var detailsElement) || detailsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var detail in detailsElement.EnumerateArray())
            {
                if (!detail.TryGetProperty("retryDelay", out var retryDelayElement))
                {
                    continue;
                }

                var retryDelayText = retryDelayElement.GetString();
                if (string.IsNullOrWhiteSpace(retryDelayText))
                {
                    continue;
                }

                retryDelayText = retryDelayText.Trim();
                if (retryDelayText.EndsWith('s') &&
                    double.TryParse(retryDelayText[..^1], out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
