using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kindred.SharedKernel.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>RabbitMQ publisher + (if any handlers get registered) the consumer background service.</summary>
    public static IServiceCollection AddKindredMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        services.AddSingleton<IntegrationEventRegistry>();
        services.AddHostedService<RabbitMqConsumerService>();
        return services;
    }

    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddSingleton(new IntegrationEventRegistration(typeof(TEvent)));
        return services;
    }
}

/// <summary>Marker collected at startup into the <see cref="IntegrationEventRegistry"/>.</summary>
public sealed record IntegrationEventRegistration(Type EventType);
