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

        protected MultiStreamConnection OutgoingConnection => _outgoingConnection!;

        protected IServicePrx Proxy => IServicePrx.FromPath("/dummy", ClientEndpoint.Protocol);
        protected MultiStreamConnection IncomingConnection => _incomingConnection!;
        protected MultiStreamConnectionType ConnectionType { get; }
        private MultiStreamConnection? _outgoingConnection;
        private RpcStream? _controlStreamForClient;
        private RpcStream? _controlStreamForServer;
        private RpcStream? _peerControlStreamForClient;
        private RpcStream? _peerControlStreamForServer;
        private MultiStreamConnection? _incomingConnection;

        public MultiStreamConnectionBaseTest(MultiStreamConnectionType connectionType)
            : base(connectionType == MultiStreamConnectionType.Ice1 ? Protocol.Ice1 : Protocol.Ice2,
                   connectionType == MultiStreamConnectionType.Coloc ? "coloc" : "tcp",
                   tls: false) =>
            ConnectionType = connectionType;

        public async Task SetUpConnectionsAsync()
        {
            Task<MultiStreamConnection> acceptTask = AcceptAsync();
            _outgoingConnection = await ConnectAsync();
            _incomingConnection = await acceptTask;

            ValueTask initializeTask = _incomingConnection.InitializeAsync(default);
            await _outgoingConnection.InitializeAsync(default);
            await initializeTask;

            _controlStreamForClient = await OutgoingConnection.SendInitializeFrameAsync(default);
            _controlStreamForServer = await IncomingConnection.SendInitializeFrameAsync(default);

            _peerControlStreamForClient = await OutgoingConnection.ReceiveInitializeFrameAsync(default);
            _peerControlStreamForServer = await IncomingConnection.ReceiveInitializeFrameAsync(default);
        }

        public void TearDownConnections()
        {
            _controlStreamForClient?.Release();
            _peerControlStreamForClient?.Release();
            _controlStreamForServer?.Release();
            _peerControlStreamForServer?.Release();

            _outgoingConnection?.Dispose();
            _incomingConnection?.Dispose();
        }

        static protected OutgoingResponse GetResponseFrame(IncomingRequest request) =>
            // TODO: Fix once OutgoingRespongFrame construction is simplified to not depend on Current
            new(request, new UnhandledException(new InvalidOperationException()));
    }
}
