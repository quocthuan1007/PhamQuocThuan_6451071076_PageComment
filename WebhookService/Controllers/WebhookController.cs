using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using WebhookService.Services;

namespace WebhookService.Controllers;

[ApiController]
[Route("[controller]")]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IFacebookSignatureValidator _signatureValidator;
    private readonly IPayloadParserService _payloadParser;
    private readonly IKafkaProducerService _kafkaProducer;

    public WebhookController(
        ILogger<WebhookController> logger,
        IConfiguration configuration,
        IFacebookSignatureValidator signatureValidator,
        IPayloadParserService payloadParser,
        IKafkaProducerService kafkaProducer)
    {
        _logger = logger;
        _configuration = configuration;
        _signatureValidator = signatureValidator;
        _payloadParser = payloadParser;
        _kafkaProducer = kafkaProducer;
    }

    [HttpGet]
    public IActionResult VerifyWebhook([FromQuery(Name = "hub.mode")] string mode,
                                       [FromQuery(Name = "hub.verify_token")] string verifyToken,
                                       [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var configuredToken = _configuration["Facebook:VerifyToken"];

        if (mode == "subscribe" && verifyToken == configuredToken)
        {
            _logger.LogInformation("Webhook verified successfully.");
            return Ok(challenge);
        }

        _logger.LogWarning("Webhook verification failed.");
        return Forbid();
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveEvent()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();

        _logger.LogInformation($"[DEBUG] Đã nhận POST request từ Facebook!");
        _logger.LogInformation($"[DEBUG] Payload: {payload}");

        var signatureHeader = Request.Headers["X-Hub-Signature-256"].ToString();

        if (!_signatureValidator.IsValidSignature(payload, signatureHeader))
        {
            _logger.LogWarning("Invalid Facebook signature.");
            return Unauthorized("Invalid signature.");
        }

        var normalizedEvents = _payloadParser.ParsePayload(payload);

        foreach (var evt in normalizedEvents)
        {
            await _kafkaProducer.PublishEventAsync(evt);
        }

        return Ok("EVENT_RECEIVED");
    }
}
