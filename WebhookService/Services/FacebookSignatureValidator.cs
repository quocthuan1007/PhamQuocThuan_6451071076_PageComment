using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace WebhookService.Services;

public interface IFacebookSignatureValidator
{
    bool IsValidSignature(string payload, string signatureHeader);
}

public class FacebookSignatureValidator : IFacebookSignatureValidator
{
    private readonly string _appSecret;

    public FacebookSignatureValidator(IConfiguration configuration)
    {
        _appSecret = configuration["Facebook:AppSecret"] ?? "dummy_secret";
    }

    public bool IsValidSignature(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256="))
            return false;

        var signature = signatureHeader.Substring(7);
        var secretBytes = Encoding.UTF8.GetBytes(_appSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using (var hmac = new HMACSHA256(secretBytes))
        {
            var hashBytes = hmac.ComputeHash(payloadBytes);
            var hashString = Convert.ToHexString(hashBytes).ToLower();
            return hashString == signature;
        }
    }
}
