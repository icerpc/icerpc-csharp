// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using System.Buffers;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>A middleware that validates an identity token request field and adds an identity feature to the request's
/// feature collection.</summary>
internal class AuthenticationMiddleware : IDispatcher
{
    private readonly IBearerAuthenticationHandler _bearerAuthenticationHandler;
    private readonly IDispatcher _next;

    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields.TryGetValue(IdentityTokenFieldKey.Value, out ReadOnlySequence<byte> buffer))
        {
            IIdentityFeature identityFeature = await _bearerAuthenticationHandler.ValidateIdentityTokenAsync(buffer);
            request.Features = request.Features.With(identityFeature);
        }
        return await _next.DispatchAsync(request, cancellationToken);
    }

    /// <summary>Constructs an authentication middleware.</summary>
    /// <param name="next">The dispatcher to call next.</param>
    /// <param name="bearerAuthenticationHandler">The bearer authentication handler to validate the identity
    /// token.</param>
    internal AuthenticationMiddleware(IDispatcher next, IBearerAuthenticationHandler bearerAuthenticationHandler)
    {
        _next = next;
        _bearerAuthenticationHandler = bearerAuthenticationHandler;
    }
}
