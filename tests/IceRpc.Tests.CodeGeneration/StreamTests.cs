// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration.Stream
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public class StreamTests
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly IStreamsPrx _prx;

        public StreamTests()
        {
            _server = new Server
            {
                Dispatcher = new Streams(),
                Endpoint = TestHelper.GetUniqueColocEndpoint(Protocol.Ice2)
            };
            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
            _prx = IStreamsPrx.FromConnection(_connection);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _connection.ShutdownAsync();
        }

        [Test]
        public async Task Streams_Byte()
        {
            System.IO.Stream stream;
            stream = await _prx.OpStreamByteReceive0Async();
            byte[] buffer = new byte[512];
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
        }

        public class Streams : IStreams
        {
            public ValueTask<System.IO.Stream> OpStreamByteReceive0Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new MemoryStream(new byte[256]));

            public ValueTask<(byte, System.IO.Stream)> OpStreamByteReceive1Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, new MemoryStream(new byte[256])));

            public ValueTask<(byte, int, System.IO.Stream)> OpStreamByteReceive2Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, 6, new MemoryStream(new byte[256])));

            public ValueTask OpStreamByteSend0Async(
                System.IO.Stream p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new();

            public ValueTask OpStreamByteSend1Async(
                byte p1,
                System.IO.Stream p2,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new();

            public ValueTask OpStreamByteSend2Async(
                byte p1,
                int p2,
                System.IO.Stream p3,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new();

            public ValueTask<System.IO.Stream> OpStreamByteSendReceive0Async(
                System.IO.Stream p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(p1);

            public ValueTask<(byte, System.IO.Stream)> OpStreamByteSendReceive1Async(
                byte p1,
                System.IO.Stream p2,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((p1, p2));

            public ValueTask<(byte, int, System.IO.Stream)> OpStreamByteSendReceive2Async(
                byte p1,
                int p2,
                System.IO.Stream p3,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((p1, p2, p3));
        }
    }
}
