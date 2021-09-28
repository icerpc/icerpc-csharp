// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Tests
{
    /// <summary>A network socket stub</summary>
    internal class NetworkSocketStub : NetworkSocket
    {
        public override bool IsDatagram => _isDatagram;
        public bool Disposed { get; private set; }

        internal Endpoint? Endpoint { get; private set; }

        private readonly bool _isDatagram;

        public override ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            Endpoint = endpoint;
            return new(endpoint);
        }

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            Endpoint?.Params.SequenceEqual(remoteEndpoint.Params) ?? false;

        public override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel) =>
            new(buffer.Length);

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
            default;

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
        public bool IsServer { get;}
        public TimeSpan LastActivity => throw new NotImplementedException();
        public Endpoint? LocalEndpoint { get; }
        public ILogger Logger => throw new NotImplementedException();
        public Endpoint? RemoteEndpoint { get; }

        public ValueTask ConnectAsync(CancellationToken cancel) => default;

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => throw new NotImplementedException();

        public ValueTask<IMultiStreamConnection> GetMultiStreamConnectionAsync(CancellationToken _) =>
            throw new NotImplementedException();

        public ValueTask<ISingleStreamConnection> GetSingleStreamConnectionAsync(CancellationToken _) =>
            throw new NotImplementedException();

        public void Dispose()
        {
        }

        public NetworkConnectionStub(Endpoint localEndpoint, Endpoint remoteEndpoint, bool isServer)
        {
            IsServer = isServer;
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
        }
    }

    public static class ConnectionStub
    {
        /// <summary>Creates a connection stub to provide the local and remote endpoint properties for a
        /// connection.</summary>
        public static Connection Create(Endpoint localEndpoint, Endpoint remoteEndpoint, bool isServer) =>
#pragma warning disable CA2000
            new(new NetworkConnectionStub(
                    localEndpoint,
                    remoteEndpoint,
                    isServer),
                dispatcher: null,
                new(),
                loggerFactory: null);
#pragma warning restore CA2000
    }
}
