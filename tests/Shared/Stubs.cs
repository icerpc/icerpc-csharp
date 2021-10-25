// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Tests
{
    /// <summary>A network socket stub</summary>
    internal class NetworkSocketStub : NetworkSocket
    {
        internal override bool IsDatagram => _isDatagram;
        public bool Connected { get; private set; }
        public bool Disposed { get; private set; }
        internal Endpoint? Endpoint { get; private set; }

        private readonly bool _isDatagram;

        internal override ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            Endpoint = endpoint;
            Connected = true;
            return new(endpoint);
        }

        internal override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            Endpoint?.Params.SequenceEqual(remoteEndpoint.Params) ?? false;

        internal override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel) =>
            new(buffer.Length);

        internal override ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
             default;

#pragma warning disable CA2215 // can't call base.Dispose(disposing) because Socket is null
        protected override void Dispose(bool disposing) => Disposed = true;
#pragma warning restore CA2215

        internal NetworkSocketStub(bool isDatagram) :
            base(null!) => _isDatagram = isDatagram;
    }
}
