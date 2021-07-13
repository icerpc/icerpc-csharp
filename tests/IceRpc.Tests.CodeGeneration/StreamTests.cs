// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA2000 // TODO Dispose MemoryStream used for Stream params

namespace IceRpc.Tests.CodeGeneration.Stream
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("slic")]
    [TestFixture("coloc")]
    public sealed class StreamTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly IStreamsPrx _prx;
        private readonly byte[] _sendBuffer;

        public StreamTests(string transport)
        {
            _sendBuffer = new byte[256];
            new Random().NextBytes(_sendBuffer);

            if (transport == "coloc")
            {
                _server = new Server
                {
                    Dispatcher = new Streams(_sendBuffer),
                    Endpoint = TestHelper.GetUniqueColocEndpoint(Protocol.Ice2),
                };
            }
            else
            {
                _server = new Server
                {
                    Dispatcher = new Streams(_sendBuffer),
                    Endpoint = TestHelper.GetTestEndpoint(protocol: Protocol.Ice2),
                    HostName = "127.0.0.1"
                };
            }

            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
            _prx = IStreamsPrx.FromConnection(_connection);
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public async Task Streams_Byte()
        {
            System.IO.Stream stream;
            byte r1;
            int r2;
            byte[] buffer = new byte[512];

            stream = await _prx.OpStreamByteReceive0Async();
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(0));
            stream.Dispose();

            (r1, stream) = await _prx.OpStreamByteReceive1Async();
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x05));
            stream.Dispose();

            (r1, r2, stream) = await _prx.OpStreamByteReceive2Async();
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x05));
            Assert.That(r2, Is.EqualTo(6));
            stream.Dispose();

            await _prx.OpStreamByteSend0Async(new MemoryStream(_sendBuffer));
            await _prx.OpStreamByteSend1Async(0x08, new MemoryStream(_sendBuffer));
            await _prx.OpStreamByteSend2Async(0x08, 10, new MemoryStream(_sendBuffer));

            stream = await _prx.OpStreamByteSendReceive0Async(new MemoryStream(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(0));
            stream.Dispose();

            (r1, stream) = await _prx.OpStreamByteSendReceive1Async(0x08, new MemoryStream(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x08));
            stream.Dispose();

            (r1, r2, stream) = await _prx.OpStreamByteSendReceive2Async(
                0x08,
                10,
                new MemoryStream(_sendBuffer));
            Assert.That(stream.Read(buffer, 0, 512), Is.EqualTo(256));
            Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
            Assert.That(r1, Is.EqualTo(0x08));
            Assert.That(r2, Is.EqualTo(10));
            stream.Dispose();
        }

        public class Streams : Service, IStreams
        {
            private readonly byte[] _sendBuffer;

            public ValueTask<System.IO.Stream> OpStreamByteReceive0Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new MemoryStream(_sendBuffer));

            public ValueTask<(byte, System.IO.Stream)> OpStreamByteReceive1Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, new MemoryStream(_sendBuffer)));

            public ValueTask<(byte, int, System.IO.Stream)> OpStreamByteReceive2Async(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new((0x05, 6, new MemoryStream(_sendBuffer)));

            public ValueTask OpStreamByteSend0Async(
                System.IO.Stream p1,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                byte[] buffer = new byte[512];
                Assert.That(p1.Read(buffer, 0, 512), Is.EqualTo(256));
                Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
                return new();
            }

            public ValueTask OpStreamByteSend1Async(
                byte p1,
                System.IO.Stream p2,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                byte[] buffer = new byte[512];
                Assert.That(p2.Read(buffer, 0, 512), Is.EqualTo(256));
                Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
                return new();
            }

            public ValueTask OpStreamByteSend2Async(
                byte p1,
                int p2,
                System.IO.Stream p3,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                byte[] buffer = new byte[512];
                Assert.That(p3.Read(buffer, 0, 512), Is.EqualTo(256));
                Assert.That(buffer[..256], Is.EqualTo(_sendBuffer));
                return new();
            }

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

            public Streams(byte[] buffer) => _sendBuffer = buffer;
        }
    }
}
