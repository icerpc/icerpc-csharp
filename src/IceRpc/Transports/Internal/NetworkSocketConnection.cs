// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>An abstract multi-stream connection which is using a network socket for receiving and sending data.
    /// </summary>
    internal abstract class NetworkSocketConnection : MultiStreamConnection
    {
        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation => Underlying.ConnectionInformation;

        internal NetworkSocket Underlying { get; private set; }

        public override string ToString() => $"{base.ToString()} ({Underlying})";

        public override async ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Endpoint? remoteEndpoint = await Underlying.AcceptAsync(
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
            LocalEndpoint = await Underlying.ConnectAsync(
                RemoteEndpoint!,
                authenticationOptions,
                cancel).ConfigureAwait(false);

        protected override void Dispose(bool disposing)
        {
            // First dispose of the underlying connection otherwise base.Dispose() which releases the stream can trigger
            // additional data to be sent of the stream release sends data (which is the case for SlicStream).
            if (disposing)
            {
                Underlying.Dispose();
            }
            base.Dispose(disposing);
        }

        protected NetworkSocketConnection(
            Endpoint endpoint,
            NetworkSocket networkSocket,
            ConnectionOptions options)
            : base(endpoint, options, networkSocket.Logger) => Underlying = networkSocket;
    }
}
