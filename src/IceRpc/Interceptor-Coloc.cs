// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Linq;

namespace IceRpc
{
    public static partial class Interceptor
    {
        /// <summary>Returns the coloc interceptor. This interceptor is no-op when the request carries a connection;
        /// otherwise, it converts each endpoint of the request into its coloc counterpart when available.
        /// See <see cref="Server.HasColocEndpoint"/>. It should be installed just before <see cref="Binder"/>.
        /// </summary>
        /// <value>The coloc interceptor.</value>
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
