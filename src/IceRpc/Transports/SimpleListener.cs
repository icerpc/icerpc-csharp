// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>The base class for simple tansport listener.</summary>
    public abstract class SimpleListener : IListener
    {
        /// <inheritdoc/>
        public abstract Endpoint Endpoint { get; }

        /// <summary>Creates a network connection that supports <see cref="ISimpleStream"/>. Multiplexed stream support
        /// is provided by Slic.</summary>
        /// <returns>The <see cref="SimpleNetworkConnection"/>.</returns>
        public abstract Task<SimpleNetworkConnection> AcceptAsync();

        /// <inheritdoc/>
        public abstract void Dispose();

        async Task<INetworkConnection> IListener.AcceptAsync() => await AcceptAsync().ConfigureAwait(false);
    }
}
