using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedDomain;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoreService.Services;

public interface IAiClassificationService
{
    Task<(IntentType Intent, SentimentType Sentiment)> ClassifyAsync(string content);
}

public class GeminiClassificationService : IAiClassificationService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiClassificationService> _logger;
    private readonly KeywordFallbackClassificationService _fallbackService;

    public GeminiClassificationService(
        IConfiguration configuration,
        ILogger<GeminiClassificationService> logger,
        KeywordFallbackClassificationService fallbackService)
    {
        _configuration = configuration;
        _logger = logger;
        _fallbackService = fallbackService;
    }

    public async Task<(IntentType Intent, SentimentType Sentiment)> ClassifyAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (IntentType.Unknown, SentimentType.Neutral);
        }

        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Gemini API key is missing. Falling back to keyword classifier.");
            return await _fallbackService.ClassifyAsync(content);
        }

        try
        {
            var endpoint = _configuration["Gemini:Endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta";
            var model = _configuration["Gemini:Model"] ?? "gemini-2.5-flash-lite";

            using var request = BuildRequest(apiKey, endpoint, model, content);
            using var response = await HttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Gemini classify failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
            }

            var parsed = ParseClassificationFromResponse(body);
            if (parsed is null)
            {
                throw new InvalidOperationException("Gemini response did not contain valid classification JSON.");
            }

            _logger.LogInformation(
                "[Gemini API] Classified comment with model {Model}. Intent={Intent}, Sentiment={Sentiment}",
                model,
                parsed.Value.Intent,
                parsed.Value.Sentiment);

            return parsed.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini classification failed. Falling back to keyword classifier.");
            return await _fallbackService.ClassifyAsync(content);
        }
    }

    private static HttpRequestMessage BuildRequest(string apiKey, string endpoint, string model, string content)
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
                            "You classify Vietnamese Facebook comments. " +
                            "Understand slang, abbreviations, misspellings, sarcasm, no-accent text, and short comments. " +
                            "Return only JSON with intent and sentiment. " +
                            "intent must be one of: Unknown, PriceInquiry, Complaint, Compliment, Spam. " +
                            "sentiment must be one of: Neutral, Negative, Positive. " +
                            "Examples: 'page rach' => Complaint + Negative, 'ib gia' => PriceInquiry + Neutral."
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
                            text = $"Comment: {content}"
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json",
                responseJsonSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        intent = new
                        {
                            type = "string",
                            @enum = new[] { "Unknown", "PriceInquiry", "Complaint", "Compliment", "Spam" }
                        },
                        sentiment = new
                        {
                            type = "string",
                            @enum = new[] { "Neutral", "Negative", "Positive" }
                        }
                    },
                    required = new[] { "intent", "sentiment" }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        return request;
    }

    private static (IntentType Intent, SentimentType Sentiment)? ParseClassificationFromResponse(string body)
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

        using var classificationDocument = JsonDocument.Parse(content);
        var intentRaw = classificationDocument.RootElement.GetProperty("intent").GetString();
        var sentimentRaw = classificationDocument.RootElement.GetProperty("sentiment").GetString();

        if (!Enum.TryParse<IntentType>(intentRaw, ignoreCase: true, out var intent))
        {
            intent = IntentType.Unknown;
        }

        if (!Enum.TryParse<SentimentType>(sentimentRaw, ignoreCase: true, out var sentiment))
        {
            sentiment = SentimentType.Neutral;
        }

        return (intent, sentiment);
    }
}

public class KeywordFallbackClassificationService : IAiClassificationService
{
    private static readonly string[] PricePhrases =
    [
        "gia",
        "bao nhieu",
        "bao gia",
        "price",
        "xin gia",
        "ib gia",
        "inbox gia",
        "gia sao",
        "bn",
        "bao tien",
        "gia ntn",
        "cho xin gia",
        "co gia khong"
    ];

    private static readonly string[] ComplaintPhrases =
    [
        "rach",
        "page rach",
        "shop rach",
        "te",
        "qua te",
        "that vong",
        "chan",
        "chan vay",
        "chan vai",
        "loi",
        "lom",
        "dom",
        "deo on",
        "khong on",
        "kh ong",
        "khong ok",
        "khong duoc",
        "dich vu te",
        "giao cham",
        "qua lau",
        "bo tay",
        "mat day",
        "xau",
        "kem chat luong",
        "nhu cc",
        "sida",
        "tien mat tat mang",
        "lam an chan",
        "lua dao",
        "scam",
        "bad",
        "terrible"
    ];

    private static readonly string[] ComplimentPhrases =
    [
        "tot",
        "rat tot",
        "ok",
        "ok lam",
        "on ap",
        "xin",
        "xin qua",
        "xinh",
        "xinh qua",
        "xin so",
        "uy tin",
        "chat luong",
        "dang tien",
        "tuyet voi",
        "hai long",
        "ung ho",
        "se quay lai",
        "good",
        "nice",
        "love",
        "yeu shop"
    ];

    private static readonly string[] NegativeSignals =
    [
        "ko",
        "k",
        "khong",
        "chua",
        "deo",
        "vcl",
        "vai",
        "vl",
        "cc",
        "wtf",
        "?"
    ];

    private static readonly string[] PositiveSignals =
    [
        "thanks",
        "cam on",
        "thich",
        "me",
        "ung",
        "duyet",
        "hehe",
        "hihi",
        "<3"
    ];

    public Task<(IntentType Intent, SentimentType Sentiment)> ClassifyAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult((IntentType.Unknown, SentimentType.Neutral));
        }

        var normalized = Normalize(content);
        var compact = CollapseWhitespace(normalized);

        var priceScore = ScoreMatches(compact, PricePhrases);
        var complaintScore = ScoreMatches(compact, ComplaintPhrases);
        var complimentScore = ScoreMatches(compact, ComplimentPhrases);
        var negativeScore = complaintScore + ScoreSignalWords(compact, NegativeSignals);
        var positiveScore = complimentScore + ScoreSignalWords(compact, PositiveSignals);

        if (LooksLikeQuestion(content) && priceScore > 0)
        {
            priceScore += 2;
        }

        if (compact.Contains("page rach", StringComparison.Ordinal) || compact.Contains("shop rach", StringComparison.Ordinal))
        {
            complaintScore += 3;
            negativeScore += 3;
        }

        if (ContainsStandaloneWord(compact, "xin") && !ContainsAny(compact, "loi", "loi qua", "that vong"))
        {
            complimentScore += 1;
            positiveScore += 1;
        }

        var sentiment = ResolveSentiment(negativeScore, positiveScore);
        var intent = ResolveIntent(priceScore, complaintScore, complimentScore, sentiment);

        return Task.FromResult((intent, sentiment));
    }

    private static IntentType ResolveIntent(int priceScore, int complaintScore, int complimentScore, SentimentType sentiment)
    {
        if (priceScore >= 2 && priceScore >= complaintScore && priceScore >= complimentScore)
        {
            return IntentType.PriceInquiry;
        }

        if (complaintScore >= 2 || sentiment == SentimentType.Negative)
        {
            return IntentType.Complaint;
        }

        if (complimentScore >= 2 || sentiment == SentimentType.Positive)
        {
            return IntentType.Compliment;
        }

        return IntentType.Unknown;
    }

    private static SentimentType ResolveSentiment(int negativeScore, int positiveScore)
    {
        if (negativeScore >= positiveScore + 1 && negativeScore >= 2)
        {
            return SentimentType.Negative;
        }

        if (positiveScore >= negativeScore + 1 && positiveScore >= 2)
        {
            return SentimentType.Positive;
        }

        return SentimentType.Neutral;
    }

    private static int ScoreMatches(string input, IEnumerable<string> phrases)
    {
        var score = 0;
        foreach (var phrase in phrases)
        {
            var normalizedPhrase = CollapseWhitespace(Normalize(phrase));
            if (normalizedPhrase.Length == 0)
            {
                continue;
            }

            if (ContainsPhrase(input, normalizedPhrase))
            {
                score += normalizedPhrase.Contains(' ') ? 2 : 1;
            }
        }

        return score;
    }

    private static int ScoreSignalWords(string input, IEnumerable<string> words)
    {
        var score = 0;
        foreach (var word in words)
        {
            var normalizedWord = CollapseWhitespace(Normalize(word));
            if (normalizedWord.Length == 0)
            {
                continue;
            }

            if (ContainsPhrase(input, normalizedWord))
            {
                score++;
            }
        }

        return score;
    }

    private static bool ContainsAny(string input, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (ContainsPhrase(input, CollapseWhitespace(Normalize(keyword))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPhrase(string input, string phrase)
    {
        if (phrase.Contains(' '))
        {
            return input.Contains(phrase, StringComparison.Ordinal);
        }

        return ContainsStandaloneWord(input, phrase);
    }

    private static bool ContainsStandaloneWord(string input, string word)
    {
        return Regex.IsMatch(input, $@"\b{Regex.Escape(word)}\b", RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeQuestion(string content)
    {
        return content.Contains('?')
            || content.Contains("sao", StringComparison.OrdinalIgnoreCase)
            || content.Contains("khong", StringComparison.OrdinalIgnoreCase)
            || content.Contains("ko", StringComparison.OrdinalIgnoreCase)
            || content.Contains("bn", StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string text)
    {
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lowered = text.ToLowerInvariant().Trim()
            .Replace("\u0111", "d");
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        var noAccent = sb.ToString().Normalize(NormalizationForm.FormC);
        var cleaned = new StringBuilder(noAccent.Length);

        foreach (var c in noAccent)
        {
            cleaned.Append(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ');
        }

        return cleaned.ToString();
    }
}
