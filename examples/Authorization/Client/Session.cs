// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using System.Buffers;

namespace AuthorizationExample;

/// <summary>
/// Stores the session data for the client and provides an interceptor that adds the session token to a request.
/// </summary>
public class SessionData
{
    public byte[]? Token { get; set; }

    public Func<IInvoker, IInvoker> Interceptor =>
        next => new InlineInvoker((request, cancellationToken) =>
        {
            if (Token is not null)
            {
                request.Fields = request.Fields.With(
                    (RequestFieldKey)100, new ReadOnlySequence<byte>(Token));
            }
            return next.InvokeAsync(request, cancellationToken);
        });
}
