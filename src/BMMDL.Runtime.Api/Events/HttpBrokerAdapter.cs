namespace BMMDL.Runtime.Events;

using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// HTTP webhook broker adapter — delivers domain events to registered webhook endpoints.
/// Each active webhook whose EventFilter matches the event name receives an HTTP POST.
/// Delivery failures are logged and do not propagate exceptions to the caller.
/// </summary>
public class HttpBrokerAdapter : IBrokerAdapter
{
    private readonly IWebhookStore _webhookStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpBrokerAdapter> _logger;

    public HttpBrokerAdapter(
        IWebhookStore webhookStore,
        IHttpClientFactory httpClientFactory,
        ILogger<HttpBrokerAdapter> logger)
    {
        _webhookStore = webhookStore ?? throw new ArgumentNullException(nameof(webhookStore));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "http-webhook";

    /// <inheritdoc />
    public async Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        IReadOnlyList<WebhookConfig> webhooks;
        try
        {
            webhooks = await _webhookStore.GetAllActiveAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HttpBrokerAdapter: failed to load active webhooks for event {EventName}", @event.EventName);
            return;
        }

        if (webhooks.Count == 0)
        {
            _logger.LogDebug("HttpBrokerAdapter: no active webhooks registered — discarding event {EventName}", @event.EventName);
            return;
        }

        // Build JSON payload once — shared across all matching webhooks
        var payloadObject = new
        {
            id = @event.EventId,
            eventName = @event.EventName,
            entityName = @event.EntityName,
            entityId = @event.EntityId,
            payload = @event.Payload,
            timestamp = @event.Timestamp,
            tenantId = @event.TenantId
        };
        var body = JsonSerializer.Serialize(payloadObject, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        foreach (var webhook in webhooks)
        {
            if (!MatchesFilter(webhook.EventFilter, @event.EventName))
            {
                continue;
            }

            await DeliverToWebhookAsync(webhook, @event.EventName, body, ct);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        // HTTP webhooks are stateless — no persistent connection to check.
        return Task.FromResult(true);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Delivers the serialised body to a single webhook endpoint and persists the delivery log.
    /// Never throws — all errors are caught and logged.
    /// </summary>
    private async Task DeliverToWebhookAsync(WebhookConfig webhook, string eventName, string body, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        int statusCode = 0;
        bool success = false;
        string? errorMessage = null;
        string? responseBody = null;

        try
        {
            var client = _httpClientFactory.CreateClient("WebhookDelivery");

            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.TargetUrl);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("X-BMMDL-Event", eventName);
            request.Headers.TryAddWithoutValidation("X-BMMDL-Webhook-Id", webhook.Id.ToString());

            // HMAC-SHA256 signing
            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                var signature = ComputeSignature(webhook.Secret, body);
                request.Headers.TryAddWithoutValidation("X-BMMDL-Signature", signature);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await client.SendAsync(request, cts.Token);
            sw.Stop();

            statusCode = (int)response.StatusCode;
            success = response.IsSuccessStatusCode;

            try
            {
                responseBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                if (responseBody.Length > 500)
                {
                    responseBody = responseBody[..500];
                }
            }
            catch
            {
                // Not critical — best-effort response capture
            }

            if (success)
            {
                _logger.LogDebug(
                    "HttpBrokerAdapter: delivered {EventName} to {TargetUrl} — {StatusCode} in {DurationMs}ms",
                    eventName, webhook.TargetUrl, statusCode, sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning(
                    "HttpBrokerAdapter: non-success response delivering {EventName} to {TargetUrl} — {StatusCode}",
                    eventName, webhook.TargetUrl, statusCode);
                errorMessage = $"HTTP {statusCode}";
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Delivery timed out after 10 seconds";
            _logger.LogWarning(
                "HttpBrokerAdapter: timeout delivering {EventName} to {TargetUrl}",
                eventName, webhook.TargetUrl);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = ex.Message;
            _logger.LogError(ex,
                "HttpBrokerAdapter: error delivering {EventName} to {TargetUrl}",
                eventName, webhook.TargetUrl);
        }

        // Always log delivery attempt regardless of outcome
        try
        {
            var log = new WebhookDeliveryLog
            {
                WebhookId = webhook.Id,
                EventName = eventName,
                TargetUrl = webhook.TargetUrl,
                StatusCode = statusCode,
                Success = success,
                RequestBody = body.Length > 2000 ? body[..2000] : body,
                ResponseBody = responseBody,
                ErrorMessage = errorMessage,
                DurationMs = (int)sw.ElapsedMilliseconds,
                AttemptedAt = DateTime.UtcNow
            };
            await _webhookStore.LogDeliveryAsync(log, CancellationToken.None);
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx,
                "HttpBrokerAdapter: failed to persist delivery log for webhook {WebhookId}", webhook.Id);
        }
    }

    /// <summary>
    /// Returns true if <paramref name="eventName"/> matches any entry in <paramref name="filter"/>.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    ///   <item>Empty filter → matches all events.</item>
    ///   <item>"*" → matches everything.</item>
    ///   <item>"Customer.*" → prefix wildcard (matches any event starting with "Customer.").</item>
    ///   <item>Exact string → exact match only.</item>
    /// </list>
    /// </remarks>
    internal static bool MatchesFilter(string[] filter, string eventName)
    {
        if (filter == null || filter.Length == 0)
        {
            return true;
        }

        foreach (var entry in filter)
        {
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            if (entry == "*")
            {
                return true;
            }

            if (entry.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = entry[..^1]; // "Customer." — keep the trailing dot
                if (eventName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(entry, eventName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes the HMAC-SHA256 signature for a given secret and body.
    /// Returns the value to set in the X-BMMDL-Signature header.
    /// </summary>
    private static string ComputeSignature(string secret, string body)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(keyBytes, bodyBytes);
        return $"sha256={BitConverter.ToString(hash).Replace("-", "").ToLower()}";
    }
}
