// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A dispatcher handles (dispatches) requests received by a server.</summary>
    public interface IDispatcher
    {
        public static IDispatcher FromInlineDispatcher(InlineDispatcher inlineDispatcher) =>
            new Adapter(inlineDispatcher);

        /// <summary>Dispatches a request received by a server.</summary>
        /// <param name="current">The request being dispatched.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that provides the <see cref="OutgoingResponseFrame"/> for the request.</returns>
        /// <exception cref="Exception">Any exception thrown by DispatchAsync will be marshaled into the response
        /// frame.</exception>
        public ValueTask<OutgoingResponseFrame> DispatchAsync(Current current, CancellationToken cancel) =>
            throw new NotImplementedException();

        /// <summary>Adapts an <see cref="InlineDispatcher"/> to the <see cref="IDispatcher"/> interface.</summary>
        private class Adapter : IDispatcher
        {
            private readonly InlineDispatcher _inlineDispatcher;

            /// <inheritdoc/>
            ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(Current current, CancellationToken cancel) =>
                _inlineDispatcher(current, cancel);

            /// <summary>Constructs an InlineDispatcher.</summary>
            /// <param name="inlineDispatcher">The inline dispatcher adapted by this class.</param>
            internal Adapter(InlineDispatcher inlineDispatcher) => _inlineDispatcher = inlineDispatcher;
        }
    }

    /// <summary>Delegate that provides an implementation of an <see cref="IDispatcher"/>.</summary>
    /// <param name="current">The request being dispatched.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A value task that provides the <see cref="OutgoingResponseFrame"/> for the request.</returns>
    public delegate ValueTask<OutgoingResponseFrame> InlineDispatcher(Current current, CancellationToken cancel);
}
