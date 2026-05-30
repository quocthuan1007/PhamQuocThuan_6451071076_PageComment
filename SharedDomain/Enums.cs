namespace SharedDomain;

public enum IntentType
{
    Unknown,
    PriceInquiry, // hỏi giá
    Complaint,    // khiếu nại / hỗ trợ
    Compliment,   // khen / tương tác tích cực
    Spam          // spam
}

public enum SentimentType
{
    Neutral,      // trung tính
    Negative,     // tiêu cực
    Positive      // tích cực
}

public enum EventStatus
{
    Received,
    Processed,
    Replied,
    Failed,
    Hidden,
    Blacklisted,
    ManualReview,
    Blocked
}

public enum ActionDecision
{
    None,
    AutoReply,
    ThankUser,
    ApologizeUser,
    HideComment,
    AddToBlacklist,
    SendToManualReview,
    BlockUser
}
