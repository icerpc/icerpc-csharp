// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Tests
{
    /// <summary>A network socket stub</summary>
    internal class NetworkSocketStub : NetworkSocket
    {
        public override bool IsDatagram => _isDatagram;
        public bool Connected { get; private set; }
        public bool Disposed { get; private set; }
        internal Endpoint? Endpoint { get; private set; }

        private readonly bool _isDatagram;

        public override ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            Endpoint = endpoint;
            Connected = true;
            return new(endpoint);
        }

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            Endpoint?.Params.SequenceEqual(remoteEndpoint.Params) ?? false;

        public override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel) =>
            new(buffer.Length);

        public override ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
             default;

        protected override void Dispose(bool disposing) => Disposed = true;

        internal NetworkSocketStub(bool isDatagram) :
            base(null!) => _isDatagram = isDatagram;
    }

    /// <summary>A network connection stub can be used just to provide the local and remote endpoint
    /// properties for a connection.</summary>
    internal class NetworkConnectionStub : INetworkConnection
    {
        public int DatagramMaxReceiveSize => throw new NotImplementedException();
        public TimeSpan IdleTimeout => throw new NotImplementedException();
        public bool IsDatagram => false;
        public bool IsSecure => false;
        public TimeSpan LastActivity => throw new NotImplementedException();
        public Endpoint? LocalEndpoint { get; }
        public Endpoint? RemoteEndpoint { get; }

        public void Close(Exception? exception = null)
        {
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => throw new NotImplementedException();

        public ValueTask<IMultiStreamConnection> ConnectMultiStreamConnectionAsync(CancellationToken _) =>
            throw new NotImplementedException();

        public ValueTask<ISingleStreamConnection> ConnectSingleStreamConnectionAsync(CancellationToken _) =>
            throw new NotImplementedException();

        public NetworkConnectionStub(Endpoint localEndpoint, Endpoint remoteEndpoint)
        {
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
        }
    }

    public static class ConnectionStub
    {
        /// <summary>Creates a connection stub to provide the local and remote endpoint properties for a
        /// connection.</summary>
        public static Connection Create(Endpoint localEndpoint, Endpoint remoteEndpoint) =>
            new(new NetworkConnectionStub(localEndpoint, remoteEndpoint), dispatcher: null, new());
    }
}
