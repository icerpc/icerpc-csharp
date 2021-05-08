// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Linq;

namespace IceRpc
{
    public static partial class Interceptor
    {
        public static Func<IInvoker, IInvoker> Coloc { get; } =
            next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Connection == null)
                    {
                        request.Endpoint = request.Endpoint?.GetColocCounterPart() ?? request.Endpoint;
                        request.AltEndpoints = request.AltEndpoints.Select(e => e.GetColocCounterPart() ?? e);
                    }
                    return next.InvokeAsync(request, cancel);
                });
    }
}
