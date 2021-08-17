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
        public override TimeSpan IdleTimeout
        {
            get => TimeSpan.FromSeconds(60);
            internal set => throw new NotImplementedException();
        }

        public override bool IsDatagram => false;

        public override bool? IsSecure => false;

        public MultiStreamConnectionStub(
            Endpoint localEndpoint,
            Endpoint remoteEndpoint,
            ConnectionOptions options,
            ILogger logger) :
            base(remoteEndpoint, options, logger)
        {
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
        }

        public override ValueTask AcceptAsync(
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => new();

        public override ValueTask<RpcStream> AcceptStreamAsync(CancellationToken cancel) =>
            throw new NotImplementedException();
        public override ValueTask ConnectAsync(
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => new();

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
            ConnectionOptions options = isServer ? new ServerConnectionOptions() : new ClientConnectionOptions();
            return new Connection(
                new MultiStreamConnectionStub(
                    localEndpoint,
                    remoteEndpoint,
                    options,
                    NullLogger.Instance),
                dispatcher: null,
                options,
                loggerFactory: null);
        }
    }
}
