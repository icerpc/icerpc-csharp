// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A network connection represents the low-level connection to exchange data as bytes. A network
    /// connection supports both exchanging data with an <see cref="ISimpleStream"/> (for the Ice1 protocol) or an <see
    /// cref="IMultiplexedStreamFactory"/> (for the Ice2 protocol). A simple stream based transport such as TCP or Coloc
    /// uses the Slic to provide multiplexed transport support.</summary>
    public interface INetworkConnection
    {
        /// <summary>Indicates whether or not this network connection is secure.</summary>
        /// <value><c>true</c> means the network connection is secure. <c>false</c> means the network connection is not
        /// secure. If the connection is not established, secure is always <c>false</c>.</value>
        bool IsSecure { get; }

        /// <summary>The time elapsed since the last activity of the connection.</summary>
        TimeSpan LastActivity { get; }

        /// <summary>Closes the network connection.</summary>
        /// <param name="exception">The reason of the connection closure.</param>
        void Close(Exception? exception = null);

        /// <summary>Connects this network connection and obtain an <see cref="IMultiplexedStreamFactory"/>.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="IMultiplexedStreamFactory"/> and <see
        /// cref="NetworkConnectionInformation"/>.</returns>
        Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> ConnectMultiplexedAsync(
            CancellationToken cancel);

        /// <summary>Connects this network connection and obtain an <see cref="ISimpleStream"/>.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="ISimpleStream"/> and <see cref="NetworkConnectionInformation"/>.</returns>
        Task<(ISimpleStream, NetworkConnectionInformation)> ConnectSimpleAsync(CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this network connection.
        /// Compatible means a client could reuse this network connection instead of establishing a new network
        /// connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a network connection whose parameters are compatible with the
        /// parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);
    }
}
