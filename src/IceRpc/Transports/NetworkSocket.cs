// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Text;

namespace IceRpc.Transports
{
    /// <summary>Represents a socket or socket-like object that can send and receive bytes.</summary>
    public abstract class NetworkSocket : IDisposable
    {
        /// <summary>When this socket is a datagram socket, the maximum size of a datagram received by this socket.
        /// </summary>
        public virtual int DatagramMaxReceiveSize => throw new InvalidOperationException();

        /// <summary><c>true</c> for a datagram socket; <c>false</c> otherwise.</summary>
        public abstract bool IsDatagram { get; }

        /// <summary>Indicates whether or not this socket's transport is secure.</summary>
        /// <value><c>true</c> means the socket's transport is secure. <c>false</c> means the socket's transport
        /// is not secure. And null means whether or not the transport is secure is not determined yet. This value
        /// is never null once the connection is fully established.</value>
        public abstract bool? IsSecure { get; }

        /// <summary>The underlying <see cref="SslStream"/>, if the implementation uses a ssl stream and chooses to
        /// expose it.</summary>
        public virtual SslStream? SslStream => null;

        /// <summary>The underlying socket, if the implementation uses a Socket and chooses to expose it to the test
        /// suite.</summary>
        protected internal virtual System.Net.Sockets.Socket? Socket => null;

        internal ILogger Logger { get; }

        /// <summary>Accepts a new connection. This is called after the listener accepted a new connection to perform
        /// socket level initialization (TLS handshake, etc).</summary>
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

        /// <summary>Releases the resources used by the socket.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this socket. Compatible
        /// means a client could reuse this socket (connection) instead of establishing a new connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this socket is compatible with the parameters of the provided endpoint;
        /// otherwise, <c>false</c>.</returns>
        public abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

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

        /// <inheritdoc/>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(GetType().Name);
            builder.Append(" { ");
            if (PrintMembers(builder))
            {
                builder.Append(' ');
            }
            builder.Append('}');
            return builder.ToString();
        }

        /// <summary>Releases the resources used by the socket.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected abstract void Dispose(bool disposing);

        /// <summary>Prints the fields/properties of this class using the Records format.</summary>
        /// <param name="builder">The string builder.</param>
        /// <returns><c>true</c>when members are appended to the builder; otherwise, <c>false</c>.</returns>
        protected virtual bool PrintMembers(StringBuilder builder) => false;

        internal NetworkSocket(ILogger logger) => Logger = logger;
    }
}
