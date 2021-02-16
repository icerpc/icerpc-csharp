// Copyright (c) ZeroC, Inc. All rights reserved.
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>Represents a server-side request dispatch pipeline element, and also the full pipeline itself. It's the
    /// IceRPC equivalent of Microsoft.AspNetCore.Http.RequestDelegate.</summary>
    /// <param name="request">The incoming request being dispatched.</param>
    /// <param name="current">The current object for the dispatch.</param>
    /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is cancelled.
    /// </param>
    /// <returns>The outgoing response frame.</returns>
    public delegate ValueTask<OutgoingResponseFrame> Dispatcher(
        IncomingRequestFrame request,
        Current current,
        CancellationToken cancel);
}
