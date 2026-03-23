namespace BMMDL.Runtime.Events;

/// <summary>
/// Abstraction for external message broker integration.
/// Implementations deliver outbox events to external systems (RabbitMQ, Kafka, etc.)
/// </summary>
public interface IBrokerAdapter
{
    /// <summary>
    /// Human-readable name for this broker (e.g., "rabbitmq", "kafka", "null").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Publish a domain event to the external broker.
    /// Called by OutboxProcessor after reading from the outbox table.
    /// </summary>
    Task PublishAsync(DomainEvent @event, CancellationToken ct = default);

    /// <summary>
    /// Check if the broker connection is healthy.
    /// Used by health checks and circuit breaker logic.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
