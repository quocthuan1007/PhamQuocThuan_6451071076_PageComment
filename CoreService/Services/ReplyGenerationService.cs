using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedDomain;
using System.Text;
using System.Text.Json;

namespace CoreService.Services;

public interface IReplyGenerationService
{
    Task<string> GenerateReplyAsync(
        NormalizedEvent ev,
        ActionDecision decision,
        IntentType intent,
        SentimentType sentiment,
        CancellationToken cancellationToken = default);
}

public class GeminiReplyGenerationService : IReplyGenerationService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiReplyGenerationService> _logger;

    public GeminiReplyGenerationService(
        IConfiguration configuration,
        ILogger<GeminiReplyGenerationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GenerateReplyAsync(
        NormalizedEvent ev,
        ActionDecision decision,
        IntentType intent,
        SentimentType sentiment,
        CancellationToken cancellationToken = default)
    {
        var fallbackReply = BuildFallbackReply(decision, intent, sentiment);

        var apiKey = _configuration["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Falling back to canned reply.");
            return fallbackReply;
        }

        try
        {
            var endpoint = _configuration["Gemini:Endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta";
            var model = _configuration["Gemini:Model"] ?? "gemini-2.5-flash-lite";
            using var request = BuildRequest(apiKey, endpoint, model, ev, decision, intent, sentiment);
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Gemini reply generation failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
            }

            var reply = ParseReplyFromResponse(body);
            if (string.IsNullOrWhiteSpace(reply))
            {
                throw new InvalidOperationException("Gemini response did not contain reply text.");
            }

            _logger.LogInformation("[Gemini API] Generated reply with model {Model} for event {EventId}", model, ev.EventId);
            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini reply generation failed. Falling back to canned reply.");
            return fallbackReply;
        }
    }

    private static HttpRequestMessage BuildRequest(
        string apiKey,
        string endpoint,
        string model,
        NormalizedEvent ev,
        ActionDecision decision,
        IntentType intent,
        SentimentType sentiment)
    {
        var payload = new
        {
            system_instruction = new
            {
                parts = new object[]
                {
                    new
                    {
                        text =
                            "You write short Vietnamese Facebook Page replies. " +
                            "Reply naturally, politely, and specifically to the user's comment. " +
                            "Do not mention AI. Keep it under 35 words. " +
                            "If the action is AutoReply, answer the comment directly and helpfully. " +
                            "If the action is ApologizeUser, apologize sincerely and invite inbox for support. " +
                            "If the action is ThankUser, thank warmly and stay concise. " +
                            "Return only JSON with a single field named reply."
                    }
                }
            },
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new
                        {
                            text =
                                $"Comment: {ev.Content}\n" +
                                $"ActionDecision: {decision}\n" +
                                $"Intent: {intent}\n" +
                                $"Sentiment: {sentiment}\n" +
                                "Write the best Facebook Page reply."
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.4,
                responseMimeType = "application/json",
                responseJsonSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        reply = new
                        {
                            type = "string"
                        }
                    },
                    required = new[] { "reply" }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }

    private static string? ParseReplyFromResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        using var replyDocument = JsonDocument.Parse(content);
        return replyDocument.RootElement.GetProperty("reply").GetString()?.Trim();
    }

    private static string BuildFallbackReply(ActionDecision decision, IntentType intent, SentimentType sentiment)
    {
        return decision switch
        {
            ActionDecision.AutoReply when intent == IntentType.PriceInquiry => "Cảm ơn bạn đã quan tâm. Bạn inbox giúp mình để bên mình gửi thông tin giá chi tiết nhé.",
            ActionDecision.AutoReply => "Cảm ơn bạn đã để lại bình luận. Bên mình đã ghi nhận và sẽ hỗ trợ bạn ngay nhé.",
            ActionDecision.ApologizeUser => "Bên mình rất xin lỗi vì trải nghiệm chưa tốt. Bạn để lại inbox giúp mình để bên mình hỗ trợ nhanh hơn nhé.",
            ActionDecision.ThankUser => "Cảm ơn bạn đã quan tâm và ủng hộ shop nhiều nha!",
            _ when intent == IntentType.PriceInquiry => "Cảm ơn bạn đã quan tâm. Bạn inbox giúp mình để bên mình gửi thông tin giá chi tiết nhé.",
            _ when sentiment == SentimentType.Negative => "Bên mình rất tiếc về trải nghiệm này. Bạn inbox giúp mình để hỗ trợ kỹ hơn nhé.",
            _ => "Cảm ơn bạn đã để lại bình luận cho shop nhé!"
        };
    }
}
