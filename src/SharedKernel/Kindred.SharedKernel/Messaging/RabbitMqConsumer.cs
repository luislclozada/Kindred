using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kindred.SharedKernel.Messaging;

/// <summary>The set of event types this service handles, keyed by routing key (the event type name).</summary>
public sealed class IntegrationEventRegistry(IEnumerable<IntegrationEventRegistration> registrations)
{
    public IReadOnlyDictionary<string, Type> Types { get; } = registrations
        .GroupBy(r => r.EventType.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First().EventType, StringComparer.Ordinal);
}

/// <summary>
/// One durable queue per service, bound to the routing keys of every registered handler. Each
/// message is dispatched in its own DI scope; a failed handler nacks with requeue=false so a
/// poison message cannot loop forever. Retries connecting while RabbitMQ is still starting.
/// </summary>
public sealed class RabbitMqConsumerService(
    RabbitMqConnection connection,
    IntegrationEventRegistry registry,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (registry.Types.Count == 0) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RabbitMQ not reachable yet — retrying in 3s");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken ct)
    {
        var queue = $"{options.Value.ServiceName}.events";
        var conn = await connection.GetAsync(ct);
        _channel = await conn.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(RabbitMqOptions.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        foreach (var routingKey in registry.Types.Keys)
            await _channel.QueueBindAsync(queue, RabbitMqOptions.Exchange, routingKey, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                await DispatchAsync(ea.RoutingKey, ea.Body, ct);
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Handler failed for {RoutingKey}; message dead-lettered", ea.RoutingKey);
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            }
        };

        await _channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: ct);
        logger.LogInformation("Consuming {Queue} for {Events}", queue, string.Join(", ", registry.Types.Keys));
    }

    private async Task DispatchAsync(string routingKey, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        if (!registry.Types.TryGetValue(routingKey, out var eventType)) return;

        var integrationEvent = JsonSerializer.Deserialize(Encoding.UTF8.GetString(body.Span), eventType, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Empty payload for {routingKey}");

        await using var scope = scopeFactory.CreateAsyncScope();
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        foreach (var handler in scope.ServiceProvider.GetServices(handlerType))
        {
            var task = (Task)handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.Handle))!
                .Invoke(handler, [integrationEvent, ct])!;
            await task;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
