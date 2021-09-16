// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Tests
{
    /// <summary>A network connection stub can be used just to provide the local and remote endpoint
    /// properties for a connection.</summary>
    internal class NetworkConnectionStub : INetworkConnection
    {
        public int DatagramMaxReceiveSize => throw new NotImplementedException();
        public bool IsDatagram => false;
        public bool IsSecure => false;
        public bool IsServer { get;}
        public Endpoint? LocalEndpoint { get; }
        public Endpoint? RemoteEndpoint { get; }

        public ValueTask ConnectAsync(CancellationToken cancel) => default;

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => throw new NotImplementedException();

        public ISingleStreamConnection GetSingleStreamConnection() => throw new NotImplementedException();

        public IMultiStreamConnection GetMultiStreamConnection() => throw new NotImplementedException();

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
