using SharedDomain;
using System.Threading.Tasks;

namespace CoreService.Services;

public interface IDecisionMakerService
{
    ActionDecision MakeDecision(NormalizedEvent ev, bool isSpam, int spamCount, IntentType intent, SentimentType sentiment);
}

public class DecisionMakerService : IDecisionMakerService
{
    public ActionDecision MakeDecision(NormalizedEvent ev, bool isSpam, int spamCount, IntentType intent, SentimentType sentiment)
    {
        if (isSpam)
        {
            if (spamCount >= 3)
            {
                return ActionDecision.BlockUser; // Hoặc AddToBlacklist
            }
            if (ev.Content.Contains("http")) // Link độc hại / bot
            {
                return ActionDecision.SendToManualReview; // Ẩn và đẩy sang hàng chờ
            }
            return ActionDecision.HideComment; // Spam nhẹ
        }

        // Logical conditions for normal comments based on AI
        if (intent == IntentType.Complaint && sentiment == SentimentType.Negative)
        {
            return ActionDecision.SendToManualReview; // Cần hỗ trợ ngay
        }

        return ActionDecision.None; // Bình thường, có thể reply tự động
    }
}
