// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A dispatcher handles (dispatches) incoming requests and returns outgoing responses.</summary>
    public interface IDispatcher
    {
        /// <summary>Dispatches an incoming request and returns the corresponding outgoing response.</summary>
        /// <param name="request">The incoming request being dispatched.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The corresponding <see cref="OutgoingResponse"/>.</returns>
        public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel) =>
            throw new NotImplementedException();
    }

    /// <summary>Adapts a dispatcher delegate to the <see cref="IDispatcher"/> interface.</summary>
    public class InlineDispatcher : IDispatcher
    {
        private readonly Func<IncomingRequest, CancellationToken, ValueTask<OutgoingResponse>> _function;

        /// <summary>Constructs an InlineDispatcher using a delegate.</summary>
        /// <param name="function">The function that implements the dispatcher's DispatchAsync method.</param>
        public InlineDispatcher(Func<IncomingRequest, CancellationToken, ValueTask<OutgoingResponse>> function) =>
            _function = function;

        /// <inheritdoc/>
        ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel) =>
            _function(request, cancel);
    }
}
