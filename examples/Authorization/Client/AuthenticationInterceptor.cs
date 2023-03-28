// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;

namespace AuthorizationExample;

/// <summary>An interceptor that adds the encrypted authentication token field to each request.</summary>
internal class AuthenticationInterceptor : IInvoker
{
    private readonly ReadOnlySequence<byte> _authenticationToken;
    private readonly IInvoker _next;

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        request.Fields = request.Fields.With(AuthenticationTokenFieldKey.Value, _authenticationToken);
        return _next.InvokeAsync(request, cancellationToken);
    }

    /// <summary>Constructs an authentication interceptor.</summary>
    /// <param name="next">The invoker to call next.</param>
    /// <param name="authenticationToken">The encrypted authentication token.</param>
    internal AuthenticationInterceptor(IInvoker next, ReadOnlyMemory<byte> authenticationToken)
    {
        _next = next;
        _authenticationToken = new ReadOnlySequence<byte>(authenticationToken);
    }
}
