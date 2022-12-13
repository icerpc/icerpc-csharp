// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using System.Buffers;

namespace AuthorizationExample;

/// <summary>An interceptor that adds a field with the authentication token to each request.</summary>
public class SessionInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly Guid _token;

    public SessionInterceptor(IInvoker next, Guid token)
    {
        _next = next;
        _token = token;
    }

    public Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        CancellationToken cancellationToken)
    {
        request.Fields = request.Fields.With(SessionFieldKey.Value, new ReadOnlySequence<byte>(_token.ToByteArray()));
        return _next.InvokeAsync(request, cancellationToken);
    }
}
