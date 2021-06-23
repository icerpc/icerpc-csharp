// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>Represents a socket or socket-like object that can send and receive bytes.</summary>
    public abstract class NetworkSocket : IDisposable
    {
        /// <summary>Returns information about the connection.</summary>
        public abstract ConnectionInformation ConnectionInformation { get; }

        /// <summary>When this socket is a datagram socket, the maximum size of a datagram received by this socket.
        /// </summary>
        public virtual int DatagramMaxReceiveSize => throw new InvalidOperationException();

        internal ILogger Logger { get; }

        /// <summary>This property should be used for testing purpose only.</summary>
        internal abstract System.Net.Sockets.Socket? Socket { get; }

        /// <summary>Closes the socket. The socket might use this method to send a notification to the peer
        /// of the connection closure.</summary>
        /// <param name="errorCode">The error code indicating the reason of the socket closure.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask CloseAsync(long errorCode, CancellationToken cancel);

        /// <summary>Releases the resources used by the socket.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} ({ConnectionInformation})";

        /// <summary>Accepts a new connection. This is called after the listener accepted a new connection
        /// to perform socket level initialization (TLS handshake, etc).</summary>
        /// <param name="endpoint">The endpoint used to create the connection.</param>
        /// <param name="authenticationOptions">The SSL authentication options for secure connections.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The endpoint.</returns>
        public abstract ValueTask<Endpoint?> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Connects a new client socket. This is called after the endpoint created a new socket to establish
        /// the connection and perform socket level initialization (TLS handshake, etc).</summary>
        /// <param name="endpoint">The endpoint used to create the connection.</param>
        /// <param name="authenticationOptions">The SSL authentication options for secure connections.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The endpoint.</returns>
        public abstract ValueTask<Endpoint> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Receives data from the connection.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes received.</returns>
        public abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is sent.</returns>
        public abstract ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffers">The buffers containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are sent.</returns>
        public abstract ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);

        /// <summary>Releases the resources used by the socket.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected abstract void Dispose(bool disposing);

        internal NetworkSocket(ILogger logger) => Logger = logger;
    }
}
