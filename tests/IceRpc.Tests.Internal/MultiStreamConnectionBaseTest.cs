// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

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
        protected OutgoingRequest DummyRequest =>
            new(Proxy, "foo", Payload.FromEmptyArgs(Proxy), null, DateTime.MaxValue);

        protected MultiStreamConnection ClientConnection => _clientConnection!;

        protected Proxy Proxy => Proxy.FromPath("/dummy", ClientEndpoint.Protocol);
        protected MultiStreamConnection ServerConnection => _serverConnection!;
        protected MultiStreamConnectionType ConnectionType { get; }
        private MultiStreamConnection? _clientConnection;
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

            _ = await ClientConnection.SendInitializeFrameAsync(default);
            _ = await ServerConnection.SendInitializeFrameAsync(default);

            _ = await ClientConnection.ReceiveInitializeFrameAsync(default);
            _ = await ServerConnection.ReceiveInitializeFrameAsync(default);
        }

        public void TearDownConnections()
        {
            _clientConnection?.Dispose();
            _serverConnection?.Dispose();
        }

        protected static OutgoingResponse GetResponseFrame(IncomingRequest request) =>
            // TODO: Fix once OutgoingRespongFrame construction is simplified to not depend on Current
            new(request, new UnhandledException(new InvalidOperationException()));
    }
}
