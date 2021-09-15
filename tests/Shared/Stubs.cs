// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Tests
{
    /// <summary>A MultiStreamConnection stub which can be used just to provide the local and remote endpoint
    /// properties for a connection.</summary>
    public class MultiStreamConnectionStub : MultiStreamConnection
    {
        public override bool IsDatagram => false;

        public override bool IsSecure => false;

        public MultiStreamConnectionStub(
            Endpoint localEndpoint,
            Endpoint remoteEndpoint,
            bool isServer,
            ILogger logger) :
            base(remoteEndpoint, isServer, logger)
        {
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
        }

        public override ValueTask<NetworkStream> AcceptStreamAsync(CancellationToken cancel) =>
            throw new NotImplementedException();

        public override ValueTask ConnectAsync(CancellationToken cancel) => default;

        public override NetworkStream CreateStream(bool bidirectional) =>
            throw new NotImplementedException();

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) => throw new NotImplementedException();

        public override Task PingAsync(CancellationToken cancel) => throw new NotImplementedException();
    }

    public static class ConnectionStub
    {
        /// <summary>Creates a connection stub to provide the local and remote endpoint properties for a
        /// connection.</summary>
        public static Connection Create(Endpoint localEndpoint, Endpoint remoteEndpoint, bool isServer)
        {
            return new Connection(
#pragma warning disable CA2000 // Dispose objects before losing scope
                new MultiStreamConnectionStub(
                    localEndpoint,
                    remoteEndpoint,
                    isServer,
                    NullLogger.Instance),
#pragma warning restore CA2000 // Dispose objects before losing scope
                dispatcher: null,
                new(),
                loggerFactory: null);
        }
    }
}
