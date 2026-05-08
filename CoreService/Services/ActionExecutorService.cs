using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SharedDomain;
using CoreService.Data;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;

namespace CoreService.Services;

public interface IActionExecutorService
{
    Task ExecuteActionAsync(ActionDecision decision, NormalizedEvent ev);
}

public class ActionExecutorService : IActionExecutorService
{
    private readonly ILogger<ActionExecutorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private static readonly HttpClient _httpClient = new HttpClient();

    public ActionExecutorService(ILogger<ActionExecutorService> logger, IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public async Task ExecuteActionAsync(ActionDecision decision, NormalizedEvent ev)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accessToken = _configuration["Facebook:PageAccessToken"];

        switch (decision)
        {
            case ActionDecision.HideComment:
            case ActionDecision.SendToManualReview: // Cũng ẩn nếu nghi ngờ bot/link
                _logger.LogInformation($"[FB API] Đang yêu cầu ẩn comment {ev.PlatformEventId} từ Facebook...");
                
                if (string.IsNullOrEmpty(ev.PlatformEventId))
                {
                    _logger.LogWarning("Không thể ẩn bình luận vì thiếu PlatformEventId (Comment ID).");
                    break;
                }

                try
                {
                    var url = $"https://graph.facebook.com/v19.0/{ev.PlatformEventId}?access_token={accessToken}";
                    var payload = new StringContent(JsonSerializer.Serialize(new { is_hidden = true }), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, payload);
                    
                    var responseString = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"[FB API] Đã ẩn thành công bình luận trên Facebook! Phản hồi: {responseString}");
                    }
                    else
                    {
                        _logger.LogError($"[FB API LỖI] Không thể ẩn bình luận. Mã lỗi: {response.StatusCode}, Phản hồi: {responseString}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[FB API NGOẠI LỆ] {ex.Message}");
                }

                if (decision == ActionDecision.SendToManualReview)
                {
                     _logger.LogInformation($"[Internal] Đã đưa comment {ev.EventId} vào hàng chờ xét duyệt thủ công.");
                }
                break;

            case ActionDecision.AddToBlacklist:
            case ActionDecision.BlockUser:
                _logger.LogInformation($"[Internal] Thêm user {ev.SenderId} vào Blacklist cục bộ");
                if (!dbContext.UserBlacklists.Any(u => u.SenderId == ev.SenderId))
                {
                    dbContext.UserBlacklists.Add(new UserBlacklist { SenderId = ev.SenderId, Reason = "Spam liên tục" });
                    await dbContext.SaveChangesAsync();
                }
                break;

            case ActionDecision.None:
            default:
                _logger.LogInformation($"[Auto Reply] Bỏ qua, không làm gì với comment {ev.EventId}");
                break;
        }
    }
}
