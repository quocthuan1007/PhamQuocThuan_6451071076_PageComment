using SharedDomain;

namespace BackendApi.Services;

public interface IFacebookGraphApiService
{
    Task SendCommandAsync(FacebookCommandMessage command, CancellationToken cancellationToken = default);
}

public class FacebookGraphApiService : IFacebookGraphApiService
{
    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<FacebookGraphApiService> _logger;

    public FacebookGraphApiService(IConfiguration configuration, ILogger<FacebookGraphApiService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendCommandAsync(FacebookCommandMessage command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.PlatformEventId))
        {
            throw new InvalidOperationException("PlatformEventId is required to call Facebook Graph API.");
        }

        var accessToken = _configuration["Facebook:PageAccessToken"];
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Facebook PageAccessToken is missing.");
        }

        switch (command.Decision)
        {
            case ActionDecision.AutoReply:
            case ActionDecision.ThankUser:
            case ActionDecision.ApologizeUser:
                await ReplyToCommentAsync(command, accessToken, cancellationToken);
                break;

            case ActionDecision.HideComment:
            case ActionDecision.SendToManualReview:
                await HideCommentAsync(command, accessToken, cancellationToken);
                break;

            default:
                _logger.LogInformation(
                    "Command {CommandId} has no Facebook Graph API operation for decision {Decision}.",
                    command.CommandId,
                    command.Decision);
                break;
        }
    }

    private async Task ReplyToCommentAsync(
        FacebookCommandMessage command,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ReplyMessage))
        {
            throw new InvalidOperationException("ReplyMessage is required for reply commands.");
        }

        var url = BuildGraphUrl($"{command.PlatformEventId}/comments", accessToken);
        using var response = await HttpClient.PostAsync(
            url,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["message"] = command.ReplyMessage
            }),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Facebook reply failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        _logger.LogInformation("[FB API] Replied to comment {PlatformEventId}. Body: {Body}", command.PlatformEventId, body);
    }

    private async Task HideCommentAsync(
        FacebookCommandMessage command,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url = BuildGraphUrl(command.PlatformEventId, accessToken);
        using var response = await HttpClient.PostAsync(
            url,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["is_hidden"] = "true"
            }),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Facebook hide comment failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        _logger.LogInformation("[FB API] Hidden comment {PlatformEventId}. Body: {Body}", command.PlatformEventId, body);
    }

    private string BuildGraphUrl(string path, string accessToken)
    {
        var baseUrl = _configuration["Facebook:GraphApiBaseUrl"] ?? "https://graph.facebook.com/v19.0";
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}?access_token={Uri.EscapeDataString(accessToken)}";
    }
}
