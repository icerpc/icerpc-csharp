// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using System.Buffers;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>A middleware that decodes an identity token request field and adds an identity feature to the request's
/// feature collection.</summary>
internal class AuthenticationMiddleware : IDispatcher
{
    private readonly IAuthenticationBearer _authenticationBearer;
    private readonly IDispatcher _next;

    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields.TryGetValue(IdentityTokenFieldKey.Value, out ReadOnlySequence<byte> buffer))
        {
            IIdentityFeature identityFeature = await _authenticationBearer.DecodeAndValidateIdentityTokenAsync(buffer);
            request.Features = request.Features.With(identityFeature);
        }
        return await _next.DispatchAsync(request, cancellationToken);
    }

    /// <summary>Constructs an authentication middleware.</summary>
    /// <param name="next">The dispatcher to call next.</param>
    /// <param name="authenticationBearer">The authentication bearer to decode the identity token.</param>
    internal AuthenticationMiddleware(IDispatcher next, IAuthenticationBearer authenticationBearer)
    {
        _next = next;
        _authenticationBearer = authenticationBearer;
    }
}
