// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>The base class for simple network connections.</summary>
    public abstract class SimpleNetworkConnection : INetworkConnection
    {
        /// <inheritdoc/>
        public abstract bool IsSecure { get; }
        /// <inheritdoc/>
        public abstract TimeSpan LastActivity { get; }

        /// <inheritdoc/>
        public abstract void Close(Exception? exception = null);

        /// <summary>Connects the network connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="ISimpleStream"/> and <see cref="NetworkConnectionInformation"/>.</returns>
        public abstract Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel);
        /// <inheritdoc/>

        public abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

        /// <inheritdoc/>
        Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> INetworkConnection.ConnectMultiplexedAsync(
            CancellationToken cancel) => throw new NotImplementedException();

        /// <inheritdoc/>
        Task<(ISimpleStream, NetworkConnectionInformation)> INetworkConnection.ConnectSimpleAsync(
            CancellationToken cancel) => ConnectAsync(cancel);
    }
}
