// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

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

        public override ValueTask<RpcStream> AcceptStreamAsync(CancellationToken cancel) =>
            throw new NotImplementedException();
        public override ValueTask ConnectAsync(CancellationToken cancel) => default;

        public override RpcStream CreateStream(bool bidirectional) =>
            throw new NotImplementedException();

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) => throw new NotImplementedException();

        public override ValueTask InitializeAsync(CancellationToken cancel) => throw new NotImplementedException();

        public override Task PingAsync(CancellationToken cancel) => throw new NotImplementedException();
    }

    public static class ConnectionStub
    {
        /// <summary>Creates a connection stub to provide the local and remote endpoint properties for a
        /// connection.</summary>
        public static Connection Create(Endpoint localEndpoint, Endpoint remoteEndpoint, bool isServer)
        {
            return new Connection(
                new MultiStreamConnectionStub(
                    localEndpoint,
                    remoteEndpoint,
                    isServer,
                    NullLogger.Instance),
                dispatcher: null,
                new(),
                loggerFactory: null);
        }
    }
}
