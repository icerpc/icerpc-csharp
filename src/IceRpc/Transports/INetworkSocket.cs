// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports
{
    /// <summary>Represents a socket or socket-like object that can send and receive bytes.</summary>
    public interface INetworkSocket
    {
        /// <summary>When this socket is a datagram socket, the maximum size of a datagram received by this socket.
        /// </summary>
        int DatagramMaxReceiveSize => throw new InvalidOperationException();

        /// <summary><c>true</c> for a datagram socket; <c>false</c> otherwise.</summary>
        bool IsDatagram { get; }

        /// <summary>The underlying <see cref="SslStream"/>, if the implementation uses a ssl stream and chooses to
        /// expose it.</summary>
        SslStream? SslStream { get; }

        /// <summary>The underlying socket, if the implementation uses a Socket and chooses to expose it to the test
        /// suite.</summary>
        Socket Socket { get; }

        /// <summary>Connects a new socket. This is called after the endpoint created a new socket to establish
        /// the connection and perform socket level initialization (TLS handshake, etc).</summary>
        /// <param name="endpoint">The endpoint used to create the connection.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The endpoint.</returns>
        ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this socket. Compatible
        /// means a client could reuse this socket (connection) instead of establishing a new connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this socket is compatible with the parameters of the provided endpoint;
        /// otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);

        /// <summary>Receives data from the connection.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes received.</returns>
        ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is sent.</returns>
        ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffers">The buffers containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are sent.</returns>
        ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
    }
}
