// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports
{
    /// <summary>Represents a socket or socket-like object that can send and receive bytes.</summary>
    public abstract class NetworkSocket : INetworkSocket, IDisposable
    {
        /// <inheritdoc/>
        public virtual int DatagramMaxReceiveSize => throw new InvalidOperationException();

        /// <inheritdoc/>
        public abstract bool IsDatagram { get; }

        /// <inheritdoc/>
        public Socket Socket { get; }

        /// <inheritdoc/>
        public SslStream? SslStream { get; protected set; }

        /// <inheritdoc/>
        public abstract ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel);

        /// <summary>Releases the resources used by the socket.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

        /// <inheritdoc/>
        public abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <inheritdoc/>
        public abstract ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel);

        /// <inheritdoc/>
        public abstract ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);

        /// <summary>Releases the resources used by the socket.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected virtual void Dispose(bool disposing) => Socket.Dispose();

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
