// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A network connection represents the low-level transport to exchange data as bytes.</summary>
    public interface INetworkConnection : IDisposable
    {
        /// <summary><c>true</c> for datagram connection; <c>false</c> otherwise.</summary>
        bool IsDatagram { get; }

        /// <summary>Indicates whether or not this network connection is secure.</summary>
        /// <value><c>true</c> means the network connection is secure. <c>false</c> means the network
        /// connection transport is not secure. If the connection is not established, secure is always
        /// <c>false</c>.</value>
        bool IsSecure { get; }

        /// <summary><c>true</c> for server network connections; otherwise, <c>false</c>. A server network
        /// connection is created by a server-side listener while a client network connection is created from
        /// the endpoint by the client-side.
        /// </summary>
        bool IsServer { get; }

        /// <summary>The local endpoint. The endpoint may not be available until the connection is connected.
        /// </summary>
        Endpoint? LocalEndpoint { get; }

        /// <summary>The remote endpoint. This endpoint may not be available until the connection is accepted.
        /// </summary>
        Endpoint? RemoteEndpoint { get; }

        /// <summary>Connects a new client or server network connection. This is called after the endpoint
        /// created a new connection to establish the connection and perform blocking network level
        /// initialization such as the TLS handshake or the Slic network protocol initialization.
        /// </summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        ValueTask ConnectAsync(CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this network
        /// connection. Compatible means a client could reuse this network connection instead of establishing
        /// a new network connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a client connection whose parameters are compatible
        /// with the parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);
    }
}
