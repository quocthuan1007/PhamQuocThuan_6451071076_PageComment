using System.Text.Json;
using SharedDomain;

namespace WebhookService.Services;

public interface IPayloadParserService
{
    IEnumerable<NormalizedEvent> ParsePayload(string rawPayload);
}

public class PayloadParserService : IPayloadParserService
{
    public IEnumerable<NormalizedEvent> ParsePayload(string rawPayload)
    {
        var events = new List<NormalizedEvent>();
        
        try
        {
            using var jsonDocument = JsonDocument.Parse(rawPayload);
            var root = jsonDocument.RootElement;

            if (root.TryGetProperty("entry", out var entries))
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty("messaging", out var messagings))
                    {
                        // Handle Messages
                        foreach (var messaging in messagings.EnumerateArray())
                        {
                            var senderId = messaging.GetProperty("sender").GetProperty("id").GetString();
                            if (messaging.TryGetProperty("message", out var messageObj) && messageObj.TryGetProperty("text", out var textObj))
                            {
                                events.Add(new NormalizedEvent
                                {
                                    Source = "Messenger",
                                    SenderId = senderId ?? "unknown",
                                    Content = textObj.GetString() ?? string.Empty,
                                    RawPayload = rawPayload
                                });
                            }
                        }
                    }
                    else if (entry.TryGetProperty("changes", out var changes))
                    {
                        // Handle Comments (Feed)
                        foreach (var change in changes.EnumerateArray())
                        {
                            if (change.TryGetProperty("value", out var valueObj))
                            {
                                if (valueObj.TryGetProperty("item", out var itemObj))
                                {
                                    var itemType = itemObj.GetString();
                                    // Cho phép nhận cả "comment" (bình luận) và "status" (nút Test từ Facebook / đăng bài mới)
                                    if (itemType == "comment" || itemType == "status")
                                    {
                                        string? senderId = null;
                                        string? senderName = null;
                                        string? platformEventId = null;
                                        
                                        if (valueObj.TryGetProperty("from", out var fromObj))
                                        {
                                            senderId = fromObj.GetProperty("id").GetString();
                                            senderName = fromObj.GetProperty("name").GetString();
                                        }

                                        if (valueObj.TryGetProperty("comment_id", out var commentIdObj))
                                        {
                                            platformEventId = commentIdObj.GetString();
                                        }
                                        else if (valueObj.TryGetProperty("post_id", out var postIdObj))
                                        {
                                            platformEventId = postIdObj.GetString();
                                        }

                                        var message = valueObj.TryGetProperty("message", out var msgObj) ? msgObj.GetString() : string.Empty;

                                        events.Add(new NormalizedEvent
                                        {
                                            PlatformEventId = platformEventId ?? string.Empty,
                                            Source = itemType == "status" ? "PagePost" : "PageComment",
                                            SenderId = senderId ?? "unknown",
                                            SenderName = senderName ?? "unknown",
                                            Content = message ?? string.Empty,
                                            RawPayload = rawPayload
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log parsing error, for now just ignore or return raw
            Console.WriteLine($"Error parsing payload: {ex.Message}");
        }

        return events;
    }
}
