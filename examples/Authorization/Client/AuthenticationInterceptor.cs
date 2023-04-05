// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;

namespace AuthorizationExample;

/// <summary>Represents an interceptor that adds the encrypted identity token field to each request.</summary>
internal class AuthenticationInterceptor : IInvoker
{
    private readonly ReadOnlySequence<byte> _identityToken;
    private readonly IInvoker _next;

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        request.Fields = request.Fields.With(IdentityTokenFieldKey.Value, _identityToken);
        return _next.InvokeAsync(request, cancellationToken);
    }

    /// <summary>Constructs an authentication interceptor.</summary>
    /// <param name="next">The invoker to call next.</param>
    /// <param name="identityToken">The encrypted identity token.</param>
    internal AuthenticationInterceptor(IInvoker next, ReadOnlyMemory<byte> identityToken)
    {
        _next = next;
        _identityToken = new ReadOnlySequence<byte>(identityToken);
    }
}
