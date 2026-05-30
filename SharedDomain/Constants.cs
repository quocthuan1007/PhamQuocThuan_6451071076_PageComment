namespace SharedDomain;

public static class Constants
{
    public const string KafkaTopicRawEvents = "raw_events";
    public const string KafkaTopicReplyCommands = "reply_commands";
    public const string KafkaTopicSendRetry = "send_retry";
    public const string KafkaTopicSendFailed = "send_failed";
    public const string KafkaTopicDeadLetter = "dead_letter";
}
