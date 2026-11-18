namespace Kindred.SharedKernel.Mediator;

// The in-house mediator. Deliberately tiny: commands change state, queries read it, each has
// exactly one handler. NEVER MediatR — this is the whole implementation, and it is enough.

public interface IRequest<TResponse>;

/// <summary>A request that changes state.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>A request that only reads state.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>;

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;

public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
