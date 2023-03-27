// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;

namespace AuthorizationExample;

/// <summary>An interceptor that adds a field with the authentication token to each request.</summary>
internal class AuthenticationTokenInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly byte[] _token;

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        request.Fields = request.Fields.With(AuthenticationTokenFieldKey.Value, new ReadOnlySequence<byte>(_token));
        return _next.InvokeAsync(request, cancellationToken);
    }

    internal AuthenticationTokenInterceptor(IInvoker next, byte[] token)
    {
        _next = next;
        _token = token;
    }
}
