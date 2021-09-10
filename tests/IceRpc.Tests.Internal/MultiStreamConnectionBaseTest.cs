// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    public enum MultiStreamConnectionType
    {
        Coloc,
        Slic
    }

    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class MultiStreamConnectionBaseTest : ConnectionBaseTest
    {
        protected MultiStreamConnection ClientConnection => _clientConnection!;
        protected MultiStreamConnection ServerConnection => _serverConnection!;
        protected MultiStreamConnectionType ConnectionType { get; }
        private MultiStreamConnection? _clientConnection;
        private MultiStreamConnection? _serverConnection;

        public MultiStreamConnectionBaseTest(MultiStreamConnectionType connectionType)
            : base(Protocol.Ice2, connectionType == MultiStreamConnectionType.Coloc ? "coloc" : "tcp", tls: false) =>
            ConnectionType = connectionType;

        public async Task SetUpConnectionsAsync()
        {
            Task<ITransportConnection> acceptTask = AcceptAsync();
            _clientConnection = (await ConnectAsync() as MultiStreamConnection)!;
            _serverConnection = (await acceptTask as MultiStreamConnection)!;
        }

        public void TearDownConnections()
        {
            _clientConnection?.Dispose();
            _serverConnection?.Dispose();
        }

        protected static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateSendPayload(RpcStream stream, int length = 10)
        {
            byte[] buffer = new byte[stream.TransportHeader.Length + length];
            stream.TransportHeader.CopyTo(buffer);
            return new ReadOnlyMemory<byte>[] { buffer };
        }

        protected static Memory<byte> CreateReceivePayload(int length = 10) => new byte[length];
    }
}
