// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Internal
{
    /// <summary>An abstract multi-stream socket which is using a single stream socket for receiving and sending
    /// data.</summary>
    internal abstract class MultiStreamOverSingleStreamSocket : MultiStreamSocket
    {
        /// <inheritdoc/>
        public override ISocket Socket => Underlying.Socket;

        internal SingleStreamSocket Underlying { get; private set; }

        public override string ToString() => $"{base.ToString()} ({Underlying})";

        public override async ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Endpoint? remoteEndpoint;
            (Underlying, remoteEndpoint) = await Underlying.AcceptAsync(
                LocalEndpoint!,
                authenticationOptions,
                cancel).ConfigureAwait(false);
            if (remoteEndpoint != null)
            {
                RemoteEndpoint = remoteEndpoint;
            }
        }

        public override async ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) =>
            (Underlying, LocalEndpoint) = await Underlying.ConnectAsync(
                RemoteEndpoint!,
                authenticationOptions,
                cancel).ConfigureAwait(false);

        protected override void Dispose(bool disposing)
        {
            // First dispose of the underlying socket otherwise base.Dispose() which releases the stream can trigger
            // additional data to be sent of the stream release sends data (which is the case for SlicStream).
            if (disposing)
            {
                Underlying.Dispose();
            }
            base.Dispose(disposing);
        }

        protected MultiStreamOverSingleStreamSocket(
            Endpoint endpoint,
            SingleStreamSocket socket,
            ConnectionOptions options)
            : base(endpoint, options, socket.Logger) => Underlying = socket;
    }
}
