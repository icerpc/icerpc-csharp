// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>A single-stream connection represents a network connection that supports a single stream of binary
    /// data.</summary>
    public abstract class SingleStreamConnection : IDisposable
    {
        /// <summary>Returns information about the connection.</summary>
        public abstract ConnectionInformation ConnectionInformation { get; }

        /// <summary>When this connection is a datagram connection, the maximum size of a datagram received over this
        /// connection.</summary>
        public virtual int DatagramMaxReceiveSize => throw new InvalidOperationException();

        internal ILogger Logger { get; }

        /// <summary>This property should be used for testing purpose only.</summary>
        internal abstract System.Net.Sockets.Socket? NetworkSocket { get; }

        /// <summary>Closes the connection. The connection might use this method to send a notification to the peer
        /// of the connection closure.</summary>
        /// <param name="errorCode">The error code indicating the reason of the connection closure.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask CloseAsync(long errorCode, CancellationToken cancel);

        /// <summary>Releases the resources used by the connection.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} ({ConnectionInformation})";

        /// <summary>Accepts a new incoming connection. This is called after the acceptor accepted a new connection
        /// to perform socket level initialization (TLS handshake, etc).</summary>
        /// <param name="endpoint">The endpoint used to create the connection.</param>
        /// <param name="authenticationOptions">The SSL authentication options for secure connections.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The endpoint.</returns>
        public abstract ValueTask<Endpoint?> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Connects a new outgoing connection. This is called after the endpoint created a new connection
        /// to establish the connection and perform socket level initialization (TLS handshake, etc).
        /// </summary>
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

        /// <summary>Releases the resources used by the connection.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected abstract void Dispose(bool disposing);

        internal SingleStreamConnection(ILogger logger) => Logger = logger;
    }
}
