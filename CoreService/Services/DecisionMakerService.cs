using SharedDomain;

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
                return ActionDecision.BlockUser;
            }

            if (ev.Content.Contains("http", StringComparison.OrdinalIgnoreCase))
            {
                return ActionDecision.SendToManualReview;
            }

            return ActionDecision.HideComment;
        }

        if (sentiment == SentimentType.Negative || intent == IntentType.Complaint)
        {
            return ActionDecision.ApologizeUser;
        }

        if (intent == IntentType.PriceInquiry)
        {
            return ActionDecision.AutoReply;
        }

        if (sentiment == SentimentType.Positive || intent == IntentType.Compliment)
        {
            return ActionDecision.ThankUser;
        }

        return ActionDecision.AutoReply;
    }
}
