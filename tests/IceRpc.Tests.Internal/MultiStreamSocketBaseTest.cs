// Copyright (c) ZeroC, Inc. All rights reserved.

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

        protected MultiStreamSocket ClientSocket => _clientSocket!;

        protected IServicePrx Proxy => IServicePrx.FromPath("/dummy", ClientEndpoint.Protocol);
        protected MultiStreamSocket ServerSocket => _serverSocket!;
        protected MultiStreamSocketType SocketType { get; }
        private MultiStreamSocket? _clientSocket;
        private SocketStream? _controlStreamForClient;
        private SocketStream? _controlStreamForServer;
        private SocketStream? _peerControlStreamForClient;
        private SocketStream? _peerControlStreamForServer;
        private MultiStreamSocket? _serverSocket;

        public MultiStreamSocketBaseTest(MultiStreamSocketType socketType)
            : base(socketType == MultiStreamSocketType.Ice1 ? Protocol.Ice1 : Protocol.Ice2,
                   socketType == MultiStreamSocketType.Coloc ? "coloc" : "tcp",
                   tls: false) =>
            SocketType = socketType;

        public async Task SetUpSocketsAsync()
        {
            Task<MultiStreamSocket> acceptTask = AcceptAsync();
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
