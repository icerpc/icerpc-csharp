// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A dispatcher handles (dispatches) requests received by a server.</summary>
    public interface IDispatcher
    {
        /// <summary>Dispatches a request received by a server.</summary>
        /// <param name="current">The request being dispatched.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that provides the <see cref="OutgoingResponseFrame"/> for the request.</returns>
        /// <exception cref="Exception">Any exception thrown by DispatchAsync will be marshaled into the response
        /// frame.</exception>
        public ValueTask<OutgoingResponseFrame> DispatchAsync(Current current, CancellationToken cancel) =>
            throw new NotImplementedException();
    }
}
