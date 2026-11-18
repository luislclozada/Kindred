using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Kindred.SharedKernel.Mediator;

/// <summary>
/// Resolves the single handler for a request from DI. The per-request-type wrapper is built once
/// and cached, so dispatch is one dictionary lookup plus one DI resolve.
/// </summary>
public sealed class Mediator(IServiceProvider serviceProvider) : IMediator
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = (HandlerWrapper<TResponse>)Wrappers.GetOrAdd(request.GetType(), static requestType =>
        {
            var responseType = requestType.GetInterfaces()
                .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                .GetGenericArguments()[0];
            var wrapperType = typeof(HandlerWrapper<,>).MakeGenericType(requestType, responseType);
            return Activator.CreateInstance(wrapperType)!;
        });
        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    private abstract class HandlerWrapper<TResponse>
    {
        public abstract Task<TResponse> Handle(object request, IServiceProvider sp, CancellationToken ct);
    }

    private sealed class HandlerWrapper<TRequest, TResponse> : HandlerWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Handle(object request, IServiceProvider sp, CancellationToken ct) =>
            sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>().Handle((TRequest)request, ct);
    }
}

public static class MediatorServiceCollectionExtensions
{
    /// <summary>Registers the mediator and every request handler found in the given assemblies (scoped).</summary>
    public static IServiceCollection AddKindredMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IMediator, Mediator>();

        var handlerInterface = typeof(IRequestHandler<,>);
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var iface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface))
            {
                services.AddScoped(iface, type);
            }
        }

        return services;
    }
}
