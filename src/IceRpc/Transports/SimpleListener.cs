// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>The base class for simple tansport listener.</summary>
    public abstract class SimpleListener : IListener
    {
        /// <inheritdoc/>
        public abstract Endpoint Endpoint { get; }

        /// <inheritdoc/>
        public abstract Task<SimpleNetworkConnection> AcceptAsync();

        /// <inheritdoc/>
        public abstract void Dispose();

        async Task<INetworkConnection> IListener.AcceptAsync() => await AcceptAsync().ConfigureAwait(false);
    }
}
