namespace BMMDL.Runtime.Api.Observability;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using BMMDL.Runtime.Events;

/// <summary>
/// Central metrics definition for BMMDL Runtime API.
/// Uses OpenTelemetry Metrics API for instrumentation.
/// </summary>
public class BmmdlMetrics : IEventMetrics
{
    public const string MeterName = "BMMDL.Runtime.Api";

    private readonly Meter _meter;

    // Request metrics
    private readonly Counter<long> _requestCounter;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<double> _requestDuration;

    // Auth metrics
    private readonly Counter<long> _loginAttempts;
    private readonly Counter<long> _loginSuccesses;
    private readonly Counter<long> _loginFailures;
    private readonly Counter<long> _tokenRefreshes;

    // CRUD metrics
    private readonly Counter<long> _crudOperations;
    private readonly Histogram<double> _crudDuration;

    // Cache metrics
    private readonly Counter<long> _cacheReloads;
    private readonly Histogram<double> _cacheReloadDuration;

    // Event metrics
    private readonly Counter<long> _eventsPublished;
    private readonly Counter<long> _eventsHandled;
    private readonly Histogram<double> _eventHandlerDuration;
    private readonly Counter<long> _eventsOutboxPending;
    private readonly Counter<long> _eventsDeadLetter;

    public BmmdlMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        // Request metrics
        _requestCounter = _meter.CreateCounter<long>(
            "bmmdl.requests.total",
            description: "Total number of HTTP requests");

        _errorCounter = _meter.CreateCounter<long>(
            "bmmdl.requests.errors",
            description: "Total number of request errors");

        _requestDuration = _meter.CreateHistogram<double>(
            "bmmdl.requests.duration",
            unit: "ms",
            description: "HTTP request duration in milliseconds");

        // Auth metrics
        _loginAttempts = _meter.CreateCounter<long>(
            "bmmdl.auth.login_attempts",
            description: "Total login attempts");

        _loginSuccesses = _meter.CreateCounter<long>(
            "bmmdl.auth.login_successes",
            description: "Successful logins");

        _loginFailures = _meter.CreateCounter<long>(
            "bmmdl.auth.login_failures",
            description: "Failed logins");

        _tokenRefreshes = _meter.CreateCounter<long>(
            "bmmdl.auth.token_refreshes",
            description: "Token refresh operations");

        // CRUD metrics
        _crudOperations = _meter.CreateCounter<long>(
            "bmmdl.crud.operations",
            description: "CRUD operations by type");

        _crudDuration = _meter.CreateHistogram<double>(
            "bmmdl.crud.duration",
            unit: "ms",
            description: "CRUD operation duration in milliseconds");

        // Cache metrics
        _cacheReloads = _meter.CreateCounter<long>(
            "bmmdl.cache.reloads",
            description: "Cache reload operations");

        _cacheReloadDuration = _meter.CreateHistogram<double>(
            "bmmdl.cache.reload_duration",
            unit: "ms",
            description: "Cache reload duration in milliseconds");

        // Event metrics
        _eventsPublished = _meter.CreateCounter<long>(
            "bmmdl_events_published_total",
            description: "Total number of domain events published");

        _eventsHandled = _meter.CreateCounter<long>(
            "bmmdl_events_handled_total",
            description: "Total number of domain events handled");

        _eventHandlerDuration = _meter.CreateHistogram<double>(
            "bmmdl_events_handler_duration_seconds",
            unit: "s",
            description: "Event handler execution duration in seconds");

        _eventsOutboxPending = _meter.CreateCounter<long>(
            "bmmdl_events_outbox_pending_count",
            description: "Number of outbox entries enqueued");

        _eventsDeadLetter = _meter.CreateCounter<long>(
            "bmmdl_events_dead_letter_count",
            description: "Number of events moved to dead letter");
    }

    // Request methods
    public void RecordRequest(string method, string endpoint, int statusCode, double durationMs)
    {
        var tags = new TagList
        {
            { "method", method },
            { "endpoint", endpoint },
            { "status_code", statusCode.ToString() }
        };

        _requestCounter.Add(1, tags);
        _requestDuration.Record(durationMs, tags);

        if (statusCode >= 400)
        {
            _errorCounter.Add(1, tags);
        }
    }

    // Auth methods
    public void RecordLoginAttempt() => _loginAttempts.Add(1);
    public void RecordLoginSuccess() => _loginSuccesses.Add(1);
    public void RecordLoginFailure() => _loginFailures.Add(1);
    public void RecordTokenRefresh() => _tokenRefreshes.Add(1);

    // CRUD methods
    public void RecordCrudOperation(string operation, string entity, double durationMs)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "entity", entity }
        };

        _crudOperations.Add(1, tags);
        _crudDuration.Record(durationMs, tags);
    }

    // Cache methods
    public void RecordCacheReload(double durationMs)
    {
        _cacheReloads.Add(1);
        _cacheReloadDuration.Record(durationMs);
    }

    // Event metrics (IEventMetrics implementation)
    public void RecordEventPublished(string eventName, string entityName, string? tenant)
    {
        var tags = new TagList
        {
            { "event_name", eventName },
            { "entity_name", entityName },
            { "tenant", tenant ?? "global" }
        };
        _eventsPublished.Add(1, tags);
    }

    public void RecordEventHandled(string eventName, string handler, string status, double durationMs)
    {
        var tags = new TagList
        {
            { "event_name", eventName },
            { "handler", handler }
        };
        _eventsHandled.Add(1, new TagList
        {
            { "event_name", eventName },
            { "handler", handler },
            { "status", status }
        });
        _eventHandlerDuration.Record(durationMs / 1000.0, tags);
    }

    public void RecordDeadLetter(string eventName)
    {
        _eventsDeadLetter.Add(1, new TagList { { "event_name", eventName } });
    }

    public void RecordOutboxEnqueued()
    {
        _eventsOutboxPending.Add(1);
    }
}
