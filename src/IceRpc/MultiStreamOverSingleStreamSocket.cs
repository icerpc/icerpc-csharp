// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An abstract multi-stream socket which is using a single stream socket for receiving and sending
    /// data.</summary>
    internal abstract class MultiStreamOverSingleStreamSocket : MultiStreamSocket
    {
        /// <inheritdoc/>
        public override ISocket Socket => Underlying.Socket;

        internal SingleStreamSocket Underlying { get; private set; }

        public override string ToString() => $"{base.ToString()} ({Underlying})";

        public override void Abort() => Underlying.Dispose();

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
            base.Dispose(disposing);
            if (disposing)
            {
                Underlying.Dispose();
            }
        }

        protected MultiStreamOverSingleStreamSocket(
            Endpoint endpoint,
            SingleStreamSocket socket,
            ConnectionOptions options)
            : base(endpoint, options, socket.Logger) => Underlying = socket;
    }
}
