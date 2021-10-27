// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports.Internal
{
    /// <summary>Represents a socket or socket-like object that can send and receive bytes.</summary>
    internal abstract class NetworkSocket : IDisposable
    {
        /// <summary>When this socket is a datagram socket, the maximum size of a datagram received by this socket.
        /// </summary>
        internal virtual int DatagramMaxReceiveSize => throw new InvalidOperationException();

        /// <summary><c>true</c> for a datagram socket; <c>false</c> otherwise.</summary>
        internal abstract bool IsDatagram { get; }

        /// <summary>The underlying <see cref="Socket"/>.</summary>
        internal Socket Socket { get; }

        /// <summary>The underlying <see cref="SslStream"/>, if the implementation uses a ssl stream and chooses to
        /// expose it.</summary>
        internal SslStream? SslStream { get; set; }

        /// <summary>Connects a new socket. This is called after the endpoint created a new socket to
        /// establish the connection and perform socket level initialization (TLS handshake, etc).</summary>
        /// <param name="endpoint">The endpoint used to create the connection.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The endpoint.</returns>
        internal abstract ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel);

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
        internal abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

        /// <summary>Receives data from the connection.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes received.</returns>
        internal abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Sends data over the connection.</summary>
        /// <param name="buffers">The buffers containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are sent.</returns>
        internal abstract ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);

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
        protected virtual void Dispose(bool disposing)
        {
            try
            {
                Socket.Shutdown(SocketShutdown.Both);
            }
            finally
            {
                Socket.Close();
            }
        }

        /// <summary>Prints the fields/properties of this class using the Records format.</summary>
        /// <param name="builder">The string builder.</param>
        /// <returns><c>true</c>when members are appended to the builder; otherwise, <c>false</c>.</returns>
        protected virtual bool PrintMembers(StringBuilder builder)
        {
            builder.Append("LocalEndPoint = ").Append(Socket.LocalEndPoint).Append(", ");
            builder.Append("RemoteEndPoint = ").Append(Socket.RemoteEndPoint);
            return true;
        }

        internal NetworkSocket(Socket socket) => Socket = socket;
    }
}
