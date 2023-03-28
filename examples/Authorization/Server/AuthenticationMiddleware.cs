// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using System.Buffers;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>A middleware that decodes and decrypt an authentication token request field and adds an authentication
/// feature to the request's feature collection.</summary>
internal class AuthenticationMiddleware : IDispatcher
{
    private readonly SymmetricAlgorithm _encryptionAlgorithm;
    private readonly IDispatcher _next;

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields.TryGetValue(AuthenticationTokenFieldKey.Value, out ReadOnlySequence<byte> buffer))
        {
            var token = AuthenticationToken.Decrypt(buffer.ToArray(), _encryptionAlgorithm);
            request.Features = request.Features.With<IAuthenticationFeature>(new AuthenticationFeature(token));
        }
        return _next.DispatchAsync(request, cancellationToken);
    }

    /// <summary>Constructs an authentication middleware.</summary>
    /// <param name="next">The invoker to call next.</param>
    /// <param name="encryptionAlgorithm">The encryption algorithm used to encrypt an authentication token.</param>
    internal AuthenticationMiddleware(IDispatcher next, SymmetricAlgorithm encryptionAlgorithm)
    {
        _next = next;
        _encryptionAlgorithm = encryptionAlgorithm;
    }
}
