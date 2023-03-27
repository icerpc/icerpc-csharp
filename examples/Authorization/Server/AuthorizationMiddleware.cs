// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace AuthorizationExample;

/// <summary>Middleware that checks if the request is authorized. If not, it throws a <see cref="DispatchException"
/// />.</summary>
internal class AuthorizationMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly Func<IAuthenticationFeature, bool> _authorizeFunc;

    internal AuthorizationMiddleware(IDispatcher next, Func<IAuthenticationFeature, bool> authorizeFunc)
    {
        _next = next;
        _authorizeFunc = authorizeFunc;
    }

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Features.Get<IAuthenticationFeature>() is IAuthenticationFeature authenticationFeature &&
            _authorizeFunc(authenticationFeature))
        {
            return _next.DispatchAsync(request, cancellationToken);
        }
        else
        {
            throw new DispatchException(StatusCode.Unauthorized, "Not authorized.");
        }
    }
}
