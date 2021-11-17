// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A network connection represents a transport-level connection used to exchange data as bytes.</summary>
    public interface INetworkConnection : IAsyncDisposable
    {
        /// <summary>Indicates whether or not this network connection is secure.</summary>
        /// <value><c>true</c> means the network connection is secure. <c>false</c> means the network connection is not
        /// secure. If the connection is not established, secure is always <c>false</c>.</value>
        bool IsSecure { get; }

        /// <summary>The time elapsed since the last activity of the connection.</summary>
        TimeSpan LastActivity { get; }

        /// <summary>Connects this network connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="NetworkConnectionInformation"/>.</returns>
        Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this network connection.
        /// Compatible means a client could reuse this network connection instead of establishing a new network
        /// connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a network connection whose parameters are compatible with the
        /// parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);
    }
}
