// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using System.Buffers;

namespace AuthorizationExample;

public class SessionData
{
    public byte[]? Token { get; set; }

    public Func<IInvoker, IInvoker> Interceptor =>
        next => new InlineInvoker(async (request, cancellationToken) =>
        {
            if (Token is not null)
            {
                request.Fields = request.Fields.With(
                    (RequestFieldKey)100, new ReadOnlySequence<byte>(Token));
            }
            return await next.InvokeAsync(request, cancellationToken);
        });
}
