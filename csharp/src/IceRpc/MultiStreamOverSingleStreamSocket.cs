// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An abstract multi-stream socket which is using a single stream socket for receiving and sending
    /// data.</summary>
    internal abstract class MultiStreamOverSingleStreamSocket : MultiStreamSocket
    {
        internal SingleStreamSocket Underlying { get; private set; }

        public override string ToString() => Underlying.ToString()!;

        public override void Abort() => Underlying.Dispose();

        public override async ValueTask AcceptAsync(CancellationToken cancel) =>
            Underlying = await Underlying.AcceptAsync(Endpoint, cancel).ConfigureAwait(false);

        public override async ValueTask ConnectAsync(bool secure, CancellationToken cancel) =>
            Underlying = await Underlying.ConnectAsync(Endpoint, secure, cancel).ConfigureAwait(false);

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
            Server? server,
            SingleStreamSocket socket)
            : base(endpoint, server) => Underlying = socket;

        internal override IDisposable? StartScope() => Underlying.StartScope(Endpoint);
    }
}
