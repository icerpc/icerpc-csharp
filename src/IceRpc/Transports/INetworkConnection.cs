// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>A network connection represents the low-level transport to exchange data as bytes. A network
    /// connection supports both exchanging data using an <see cref="ISingleStreamConnection"/> (for the Ice1
    /// protocol) or a <see cref="IMultiStreamConnection"/> (for the Ice2 protocol). A single-stream transport
    /// such as TCP or Coloc, uses Slic to provide multi-stream support.</summary>
    public interface INetworkConnection
    {
        /// <summary>The maximum size of a received datagram if this connection is a datagram
        /// connection.</summary>
        int DatagramMaxReceiveSize { get; }

        /// <summary>Gets the idle timeout.</summary>
        TimeSpan IdleTimeout { get; }

        /// <summary><c>true</c> for a datagram network connection; <c>false</c> otherwise.</summary>
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

        /// <summary>The time elapsed since the last activity of the connection.</summary>
        TimeSpan LastActivity { get; }

        /// <summary>The local endpoint. The endpoint may not be available until the connection is connected.
        /// </summary>
        Endpoint? LocalEndpoint { get; }

        /// <summary>The logger used by the network connection.</summary>
        ILogger Logger { get; }

        /// <summary>The remote endpoint. This endpoint may not be available until the connection is accepted.
        /// </summary>
        Endpoint? RemoteEndpoint { get; }

        /// <summary>Closes the network connection.</summary>
        /// <param name="exception">The reason of the connection closure.</param>
        void Close(Exception? exception = null);

        /// <summary>Connects a new client or server network connection. This is called after the endpoint
        /// created a new connection to establish the connection and perform blocking network level
        /// initialization such as the TLS handshake. </summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        ValueTask ConnectAsync(CancellationToken cancel);

        /// <summary>Get the single-stream connection to allow single-stream communications over this network
        /// connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="ISingleStreamConnection"/>.</returns>
        ValueTask<ISingleStreamConnection> GetSingleStreamConnectionAsync(CancellationToken cancel);

        /// <summary>Get the multi-stream connection to allow multi-stream communications over this network
        /// connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="IMultiStreamConnection"/>.</returns>
        ValueTask<IMultiStreamConnection> GetMultiStreamConnectionAsync(CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this network
        /// connection. Compatible means a client could reuse this network connection instead of establishing
        /// a new network connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a client connection whose parameters are compatible
        /// with the parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);
    }
}
