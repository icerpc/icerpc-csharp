// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A single-stream socket represents the local end of a network connection and enables transmitting
    /// raw binary data over a transport such as TCP, UDP or WebSocket.</summary>
    public abstract class SingleStreamSocket : IDisposable
    {
        internal ILogger Logger { get; }

        /// <summary>Gets the optional .NET socket associated with this single-stream socket.</summary>
        public abstract Socket? Socket { get; }

        /// <summary>Gets the optional SslStream associated with this socket.</summary>
        public abstract SslStream? SslStream { get; }

        /// <summary>Closes the socket. The socket might use this method to send a notification to the peer
        /// of the connection closure.</summary>
        /// <param name="exception">The reason of the connection closure.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        public abstract ValueTask CloseAsync(Exception exception, CancellationToken cancel);

        /// <summary>Releases the resources used by the socket.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Accept a new incoming connection. This is called after the acceptor accepted a new socket
        /// to perform socket level initialization (TLS handshake, etc).</summary>
        /// <param name="endpoint">The endpoint used to create the socket.</param>
        /// <param name="authenticationOptions">The SSL authentication options for secure sockets.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A single stream socket to use after the initialization instead of this socket. The socket
        /// implementation might return a different socket based on information read on the socket.</returns>
        public abstract ValueTask<SingleStreamSocket> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Connects a new outgoing connection. This is called after the endpoint created a new socket
        /// to establish the connection and perform socket level initialization (TLS handshake, etc).
        /// </summary>
        /// <param name="endpoint">The endpoint used to create the socket.</param>
        /// <param name="authenticationOptions">The SSL authentication options for secure sockets.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A single stream socket to use after the initialization instead of this socket. The socket
        /// implementation might return a different socket based on information read on the socket.</returns>
        public abstract ValueTask<SingleStreamSocket> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel);

        /// <summary>Receives data from the connection. This is used for stream based connections only.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The number of bytes received.</return>
        public abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Receives a new datagram from the connection, only supported for datagram connections.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The received data.</return>
        public abstract ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken cancel);

        /// <summary>Send data over the connection.</summary>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The number of bytes sent.</return>
        public abstract ValueTask<int> SendAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel);

        /// <summary>Send datagram over the connection.</summary>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The number of bytes sent.</return>
        public abstract ValueTask<int> SendDatagramAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel);

        /// <summary>Releases the resources used by the socket.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected abstract void Dispose(bool disposing);

        internal SingleStreamSocket(ILogger logger) => Logger = logger;

        /// <summary>Creates a scope that attaches info about the socket being used, the scope last until the
        /// returned object is disposed.</summary>
        /// <param name="endpoint">The endpoint that was used to create the socket.</param>
        /// <returns>A disposable that can be used to cleanup the scope.</returns>
        internal abstract IDisposable? StartScope(Endpoint endpoint);
    }
}
