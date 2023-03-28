// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace AuthorizationExample;

/// <summary>A middleware that checks if the request is authorized. If not, it throws a <see cref="DispatchException"
/// />.</summary>
internal class AuthorizationMiddleware : IDispatcher
{
    private readonly Func<IAuthenticationFeature, bool> _authorizeFunc;
    private readonly IDispatcher _next;

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

    /// <summary>Constructs an authentication middleware.</summary>
    /// <param name="next">The dispatcher to call next.</param>
    /// <param name="authorizeFunc">The authorization function.</param>
    internal AuthorizationMiddleware(IDispatcher next, Func<IAuthenticationFeature, bool> authorizeFunc)
    {
        _next = next;
        _authorizeFunc = authorizeFunc;
    }
}
