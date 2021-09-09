// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A transport connection represents a layer 4 network connection in the OSI model.</summary>
    public interface ITransportConnection : IDisposable
    {
        /// <summary><c>true</c> for datagram connection; <c>false</c> otherwise.</summary>
        bool IsDatagram { get; }

        /// <summary>Indicates whether or not this connection's transport is secure.</summary>
        /// <value><c>true</c> means the connection's transport is secure. <c>false</c> means the connection's
        /// transport is not secure. If the connection is not established, secure is always <c>false</c>.</value>
        bool IsSecure { get; }

        /// <summary><c>true</c> for server connections; otherwise, <c>false</c>. A server connection is created
        /// by a server-side listener while a client connection is created from the endpoint by the client-side.
        /// </summary>
        bool IsServer { get; }

        /// <summary>The local endpoint. The endpoint may not be available until the connection is connected.
        /// </summary>
        Endpoint? LocalEndpoint { get; }

        /// <summary>The remote endpoint. This endpoint may not be available until the connection is accepted.
        /// </summary>
        Endpoint? RemoteEndpoint { get; }

        /// <summary>Connects a new client connection. This is called after the endpoint created a new connection
        /// to establish the connection and perform blocking socket level initialization (TLS handshake, etc).
        /// </summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        ValueTask ConnectAsync(CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this connection. Compatible
        /// means a client could reuse this connection instead of establishing a new connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a client connection whose parameters are compatible with the
        /// parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);
    }
}
