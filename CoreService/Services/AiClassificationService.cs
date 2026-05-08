using SharedDomain;
using System.Threading.Tasks;

namespace CoreService.Services;

public interface IAiClassificationService
{
    Task<(IntentType Intent, SentimentType Sentiment)> ClassifyAsync(string content);
}

public class MockAiClassificationService : IAiClassificationService
{
    public Task<(IntentType Intent, SentimentType Sentiment)> ClassifyAsync(string content)
    {
        var lowerContent = content.ToLower();

        IntentType intent = IntentType.Unknown;
        SentimentType sentiment = SentimentType.Neutral;

        if (lowerContent.Contains("giá") || lowerContent.Contains("bao nhiêu"))
        {
            intent = IntentType.PriceInquiry;
            sentiment = SentimentType.Neutral;
        }
        else if (lowerContent.Contains("chưa nhận được") || lowerContent.Contains("lỗi") || lowerContent.Contains("tệ"))
        {
            intent = IntentType.Complaint;
            sentiment = SentimentType.Negative;
        }
        else if (lowerContent.Contains("hay quá") || lowerContent.Contains("tuyệt vời") || lowerContent.Contains("thích"))
        {
            intent = IntentType.Compliment;
            sentiment = SentimentType.Positive;
        }

        // Simulate network delay
        return Task.FromResult((intent, sentiment));
    }
}
