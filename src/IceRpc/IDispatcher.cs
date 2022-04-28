// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A dispatcher handles (dispatches) incoming requests and returns outgoing responses.</summary>
    public interface IDispatcher
    {
        /// <summary>Dispatches an incoming request.</summary>
        /// <param name="request">The incoming request being dispatched.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that completes when the dispatch completes.</returns>
        ValueTask DispatchAsync(IncomingRequest request, CancellationToken cancel = default);
    }

    /// <summary>Adapts a dispatcher delegate to the <see cref="IDispatcher"/> interface.</summary>
    public class InlineDispatcher : IDispatcher
    {
        private readonly Func<IncomingRequest, CancellationToken, ValueTask> _function;

        /// <summary>Constructs an InlineDispatcher using a delegate.</summary>
        /// <param name="function">The function that implements the dispatcher's DispatchAsync method.</param>
        public InlineDispatcher(Func<IncomingRequest, CancellationToken, ValueTask> function) =>
            _function = function;

        /// <inheritdoc/>
        ValueTask IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel) =>
            _function(request, cancel);
    }
}
