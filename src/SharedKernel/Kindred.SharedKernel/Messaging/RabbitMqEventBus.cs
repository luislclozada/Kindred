using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kindred.SharedKernel.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    public const string Exchange = "kindred.events";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 11672;
    public string User { get; set; } = "kindred";
    public string Password { get; set; } = "kindred_dev";

    /// <summary>Consumer queue prefix — one durable queue per service ("matching-service.events").</summary>
    public string ServiceName { get; set; } = "unknown-service";
}

/// <summary>
/// Shared connection (one per process, lazily opened). The exchange is a durable topic exchange;
/// the routing key is the event's type name, so a consumer binds exactly the events it handles.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;
        await _lock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.Host,
                Port = o.Port,
                UserName = o.User,
                Password = o.Password,
                ClientProvidedName = o.ServiceName,
            };
            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _lock.Dispose();
    }
}

public sealed class RabbitMqEventBus(RabbitMqConnection connection, ILogger<RabbitMqEventBus> logger) : IEventBus
{
    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var eventType = integrationEvent.GetType();
        // Serialize with the RUNTIME type: serializing as IIntegrationEvent would drop every payload field.
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(integrationEvent, eventType, JsonSerializerOptions.Web));

        var conn = await connection.GetAsync(cancellationToken);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(RabbitMqOptions.Exchange, ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Type = eventType.Name,
            MessageId = integrationEvent.EventId.ToString(),
        };
        await channel.BasicPublishAsync(RabbitMqOptions.Exchange, eventType.Name, mandatory: false,
            basicProperties: props, body: body, cancellationToken: cancellationToken);

        logger.LogInformation("Published {EventType} {EventId}", eventType.Name, integrationEvent.EventId);
    }
}
