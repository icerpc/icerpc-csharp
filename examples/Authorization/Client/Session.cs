// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using System.Buffers;

namespace AuthorizationExample;

/// <summary>
/// Stores the session data for the client and provides an interceptor that adds the session token to a request.
/// </summary>
public class SessionToken
{
    public byte[]? Data;
}

/// <summary>An interceptor that adds the session token to request.</summary>
public class SessionInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly SessionToken _token;

    public SessionInterceptor(IInvoker next, SessionToken token)
    {
        _next = next;
        _token = token;
    }

    public Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_token.Data is byte[] token)
        {
            request.Fields = request.Fields.With(
                (RequestFieldKey)100, new ReadOnlySequence<byte>(token));
        }
        return _next.InvokeAsync(request, cancellationToken);
    }
}
