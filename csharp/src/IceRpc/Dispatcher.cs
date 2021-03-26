// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Implements <see cref="IDispatcher"/> inline using a delegate.</summary>
    public class Dispatcher : IDispatcher
    {
        private readonly Func<Current, CancellationToken, ValueTask<OutgoingResponseFrame>> _function;

        /// <summary>Constructs a dispatcher using a delegate.</summary>
        /// <param name="function">The delegate used to implement <see cref="IDispatcher.DispatchAsync"/>.</param>
        public Dispatcher(Func<Current, CancellationToken, ValueTask<OutgoingResponseFrame>> function) =>
            _function = function;

        /// <inheritdoc/>
        ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(Current current, CancellationToken cancel) =>
            _function(current, cancel);
    }
}
