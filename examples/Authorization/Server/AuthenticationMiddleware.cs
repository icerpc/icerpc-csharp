// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using System.Buffers;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>Middleware that loads the session token from the request and adds the session feature to the request's
/// feature collection.</summary>
internal class AuthenticationMiddleware : IDispatcher
{
    private readonly SymmetricAlgorithm _cryptAlgorithm;
    private readonly IDispatcher _next;

    internal AuthenticationMiddleware(IDispatcher next, SymmetricAlgorithm cryptAlgorithm)
    {
        _next = next;
        _cryptAlgorithm = cryptAlgorithm;
    }

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields.TryGetValue(AuthenticationTokenFieldKey.Value, out ReadOnlySequence<byte> buffer))
        {
            request.Features = request.Features.With<IAuthenticationFeature>(
                new AuthenticationFeature(AuthenticationToken.Decrypt(buffer.ToArray(), _cryptAlgorithm)));
        }
        return _next.DispatchAsync(request, cancellationToken);
    }
}
