// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    public enum MultiStreamSocketType
    {
        Ice1,
        Coloc,
        Slic
    }

    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class MultiStreamSocketBaseTest : SocketBaseTest
    {
        protected OutgoingRequest DummyRequest => new(Proxy, "foo", Payload.FromEmptyArgs(Proxy), DateTime.MaxValue);

        protected MultiStreamConnection ClientSocket => _clientSocket!;

        protected IServicePrx Proxy => IServicePrx.FromPath("/dummy", ClientEndpoint.Protocol);
        protected MultiStreamConnection ServerSocket => _serverSocket!;
        protected MultiStreamSocketType SocketType { get; }
        private MultiStreamConnection? _clientSocket;
        private Stream? _controlStreamForClient;
        private Stream? _controlStreamForServer;
        private Stream? _peerControlStreamForClient;
        private Stream? _peerControlStreamForServer;
        private MultiStreamConnection? _serverSocket;

        public MultiStreamSocketBaseTest(MultiStreamSocketType socketType)
            : base(socketType == MultiStreamSocketType.Ice1 ? Protocol.Ice1 : Protocol.Ice2,
                   socketType == MultiStreamSocketType.Coloc ? "coloc" : "tcp",
                   tls: false) =>
            SocketType = socketType;

        public async Task SetUpSocketsAsync()
        {
            Task<MultiStreamConnection> acceptTask = AcceptAsync();
            _clientSocket = await ConnectAsync();
            _serverSocket = await acceptTask;

            ValueTask initializeTask = _serverSocket.InitializeAsync(default);
            await _clientSocket.InitializeAsync(default);
            await initializeTask;

            _controlStreamForClient = await ClientSocket.SendInitializeFrameAsync(default);
            _controlStreamForServer = await ServerSocket.SendInitializeFrameAsync(default);

            _peerControlStreamForClient = await ClientSocket.ReceiveInitializeFrameAsync(default);
            _peerControlStreamForServer = await ServerSocket.ReceiveInitializeFrameAsync(default);
        }

        public void TearDownSockets()
        {
            _controlStreamForClient?.Release();
            _peerControlStreamForClient?.Release();
            _controlStreamForServer?.Release();
            _peerControlStreamForServer?.Release();

            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }

        static protected OutgoingResponse GetResponseFrame(IncomingRequest request) =>
            // TODO: Fix once OutgoingRespongFrame construction is simplified to not depend on Current
            new(request, new UnhandledException(new InvalidOperationException()));
    }
}
