using System.Net;
using System.Net.Http;

namespace CoreService.Services;

public interface IFacebookApiResilienceService
{
    Task<HttpResponseMessage> ExecuteAsync(Func<Task<HttpResponseMessage>> action, CancellationToken cancellationToken);
}

public class FacebookApiResilienceService : IFacebookApiResilienceService
{
    private readonly object _lock = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private int _consecutiveFailures;
    private DateTime _openUntilUtc = DateTime.MinValue;

    public FacebookApiResilienceService(int failureThreshold, TimeSpan openDuration)
    {
        _failureThreshold = failureThreshold;
        _openDuration = openDuration;
    }

    public async Task<HttpResponseMessage> ExecuteAsync(Func<Task<HttpResponseMessage>> action, CancellationToken cancellationToken)
    {
        EnsureCircuitAllowsCall();

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await action();
                if (IsTransientFailure(response.StatusCode) && attempt < maxRetries)
                {
                    RegisterFailure();
                    await DelayWithBackoffAsync(attempt, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    RegisterFailure();
                }
                else
                {
                    RegisterSuccess();
                }

                return response;
            }
            catch (Exception ex) when (IsTransientException(ex) && attempt < maxRetries)
            {
                RegisterFailure();
                await DelayWithBackoffAsync(attempt, cancellationToken);
            }
            catch
            {
                RegisterFailure();
                throw;
            }
        }

        throw new InvalidOperationException("Retry policy exhausted without success.");
    }

    private void EnsureCircuitAllowsCall()
    {
        lock (_lock)
        {
            if (_openUntilUtc > DateTime.UtcNow)
            {
                throw new InvalidOperationException($"Circuit breaker OPEN until {_openUntilUtc:O}");
            }

            if (_openUntilUtc != DateTime.MinValue && _openUntilUtc <= DateTime.UtcNow)
            {
                _openUntilUtc = DateTime.MinValue;
                _consecutiveFailures = 0;
            }
        }
    }

    private void RegisterFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= _failureThreshold)
            {
                _openUntilUtc = DateTime.UtcNow.Add(_openDuration);
            }
        }
    }

    private void RegisterSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _openUntilUtc = DateTime.MinValue;
        }
    }

    private static bool IsTransientFailure(HttpStatusCode code)
    {
        return code == HttpStatusCode.RequestTimeout || code == HttpStatusCode.TooManyRequests || (int)code >= 500;
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException;
    }

    private static Task DelayWithBackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var seconds = (int)Math.Pow(2, attempt - 1);
        return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }
}
