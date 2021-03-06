// Copyright (c) ZeroC, Inc. All rights reserved.
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Represents a server-side request dispatch pipeline element, and also the full pipeline itself.
    /// </summary>
    /// <param name="current">The request being dispatched.</param>
    /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is cancelled.
    /// </param>
    /// <returns>The outgoing response frame.</returns>
    public delegate ValueTask<OutgoingResponseFrame> Dispatcher(Current current, CancellationToken cancel);
}
