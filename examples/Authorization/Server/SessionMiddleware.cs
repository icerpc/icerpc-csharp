// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Features;
using System.Buffers;

namespace AuthorizationExample;

/// <summary>Middleware that loads the session token from the request and adds the session feature to the request's
/// feature collection.</summary>
public class LoadSessionMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    private readonly TokenStore _tokenStore;

    public LoadSessionMiddleware(IDispatcher next, TokenStore tokenStore)
    {
        _next = next;
        _tokenStore = tokenStore;
    }

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields.TryGetValue(SessionFieldKey.Value, out ReadOnlySequence<byte> value))
        {
            var token = new Guid(value.ToArray());
            if (_tokenStore.GetName(token) is string name)
            {
                request.Features = request.Features.With<ISessionFeature>(new SessionFeature(name));
            }
        }
        return _next.DispatchAsync(request, cancellationToken);
    }
}

/// <summary>Middleware that checks if the request has a session feature. If not, it throws a
/// <see cref="DispatchException" />.</summary>
public class HasSessionMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    public HasSessionMiddleware(IDispatcher next) => _next = next;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken) =>
        request.Features.Get<ISessionFeature>() is not null ? _next.DispatchAsync(request, cancellationToken) :
        throw new DispatchException(StatusCode.Unauthorized, "Not authorized.");
}
