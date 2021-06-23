// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    public enum MultiStreamConnectionType
    {
        Ice1,
        Coloc,
        Slic
    }

    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class MultiStreamConnectionBaseTest : ConnectionBaseTest
    {
        protected OutgoingRequest DummyRequest => new(Proxy, "foo", Payload.FromEmptyArgs(Proxy), DateTime.MaxValue);

        protected MultiStreamConnection ClientConnection => _clientConnection!;

        protected IServicePrx Proxy => IServicePrx.FromPath("/dummy", ClientEndpoint.Protocol);
        protected MultiStreamConnection ServerConnection => _serverConnection!;
        protected MultiStreamConnectionType ConnectionType { get; }
        private MultiStreamConnection? _clientConnection;
        private RpcStream? _controlStreamForClient;
        private RpcStream? _controlStreamForServer;
        private RpcStream? _peerControlStreamForClient;
        private RpcStream? _peerControlStreamForServer;
        private MultiStreamConnection? _serverConnection;

        public MultiStreamConnectionBaseTest(MultiStreamConnectionType connectionType)
            : base(connectionType == MultiStreamConnectionType.Ice1 ? Protocol.Ice1 : Protocol.Ice2,
                   connectionType == MultiStreamConnectionType.Coloc ? "coloc" : "tcp",
                   tls: false) =>
            ConnectionType = connectionType;

        public async Task SetUpConnectionsAsync()
        {
            Task<MultiStreamConnection> acceptTask = AcceptAsync();
            _clientConnection = await ConnectAsync();
            _serverConnection = await acceptTask;

            ValueTask initializeTask = _serverConnection.InitializeAsync(default);
            await _clientConnection.InitializeAsync(default);
            await initializeTask;

            _controlStreamForClient = await ClientConnection.SendInitializeFrameAsync(default);
            _controlStreamForServer = await ServerConnection.SendInitializeFrameAsync(default);

            _peerControlStreamForClient = await ClientConnection.ReceiveInitializeFrameAsync(default);
            _peerControlStreamForServer = await ServerConnection.ReceiveInitializeFrameAsync(default);
        }

        public void TearDownConnections()
        {
            _controlStreamForClient?.Release();
            _peerControlStreamForClient?.Release();
            _controlStreamForServer?.Release();
            _peerControlStreamForServer?.Release();

            _clientConnection?.Dispose();
            _serverConnection?.Dispose();
        }

        static protected OutgoingResponse GetResponseFrame(IncomingRequest request) =>
            // TODO: Fix once OutgoingRespongFrame construction is simplified to not depend on Current
            new(request, new UnhandledException(new InvalidOperationException()));
    }
}
