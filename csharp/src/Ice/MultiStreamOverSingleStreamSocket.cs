// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>An abstract multi-stream socket which is using a single stream socket for receiving and sending
    /// data.</summary>
    internal abstract class MultiStreamOverSingleStreamSocket : MultiStreamSocket
    {
        internal SingleStreamSocket Underlying => _socket;
        private SingleStreamSocket _socket;

        public override string ToString() => Underlying.ToString()!;

        public override void Abort() => Underlying.Dispose();

        public override async ValueTask AcceptAsync(CancellationToken cancel) =>
            _socket = await _socket.AcceptAsync(Endpoint, cancel).ConfigureAwait(false);

        public override async ValueTask ConnectAsync(bool secure, CancellationToken cancel) =>
            _socket = await _socket.ConnectAsync(Endpoint, secure, cancel).ConfigureAwait(false);

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
            ObjectAdapter? adapter,
            SingleStreamSocket socket)
            : base(endpoint, adapter) => _socket = socket;
    }
}
