// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.IO.Compression;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [Timeout(10000)]
    [TestFixture(MultiStreamConnectionType.Slic)]
    [TestFixture(MultiStreamConnectionType.Coloc)]
    [TestFixture(MultiStreamConnectionType.Ice1)]
    public class StreamTests : MultiStreamConnectionBaseTest
    {
        public StreamTests(MultiStreamConnectionType connectionType)
            : base(connectionType)
        {
        }

        [SetUp]
        public Task SetUp() => SetUpConnectionsAsync();

        [TearDown]
        public void TearDown() => TearDownConnections();

        [TestCase(RpcStreamError.DispatchCanceled)]
        [TestCase(RpcStreamError.InvocationCanceled)]
        [TestCase((RpcStreamError)10)]
        public async Task Stream_Abort(RpcStreamError errorCode)
        {
            if (ConnectionType == MultiStreamConnectionType.Ice1)
            {
                // Not supported with Ice1
                return;
            }

            Task<(int, RpcStream)> serverTask = ReceiveAsync();

            // Create client stream and send one byte.
            RpcStream clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendAsync(CreateSendBuffer(clientStream, 1), false, default);

            (int received, RpcStream serverStream) = await serverTask;

            Assert.That(received, Is.EqualTo(1));
            var dispatchCanceled = new TaskCompletionSource();
            serverStream.CancelDispatchSource!.Token.Register(() => dispatchCanceled.SetResult());

            // Abort the stream
            clientStream.Abort(errorCode);

            // Ensure that receive on the server connection raises RpcStreamAbortedException
            RpcStreamAbortedException? ex = Assert.CatchAsync<RpcStreamAbortedException>(
                async () => await serverStream.ReceiveAsync(new byte[1], default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));
            await dispatchCanceled.Task;

            // Ensure we can still create a new stream after the cancellation
            RpcStream clientStream2 = ClientConnection.CreateStream(true);
            await clientStream2.SendAsync(CreateSendBuffer(clientStream2, 1), true, default);

            async Task<(int, RpcStream)> ReceiveAsync()
            {
                RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);

                // Continue reading from the server connection and receive the byte sent over the client stream.
                _ = ServerConnection.AcceptStreamAsync(default).AsTask();

                return (await serverStream.ReceiveAsync(new byte[256], default), serverStream);
            }
        }

        [TestCase(false, 1, 256, 256)]
        [TestCase(false, 1, 1024, 256)]
        [TestCase(false, 1, 256, 1024)]
        [TestCase(false, 1, 1024 * 1024, 1024)]
        [TestCase(false, 1, 1024, 1024 * 1024)]
        [TestCase(false, 2, 1024 * 1024, 1024)]
        [TestCase(false, 2, 1024, 1024 * 1024)]
        [TestCase(false, 3, 1024 * 1024, 1024)]
        [TestCase(false, 3, 1024, 1024 * 1024)]
        public async Task Stream_StreamSendReceiveAsync(bool flowControl, int bufferCount, int sendSize, int recvSize)
        {
            if (ConnectionType == MultiStreamConnectionType.Ice1)
            {
                // Not supported with Ice1
                return;
            }

            RpcStream clientStream = ClientConnection.CreateStream(true);
            Memory<ReadOnlyMemory<byte>> sendBuffers = new ReadOnlyMemory<byte>[bufferCount];
            byte[] buffer;
            if (bufferCount == 1)
            {
                var sendBuffer = new ArraySegment<byte>(new byte[clientStream.TransportHeader.Length + sendSize]);
                clientStream.TransportHeader.CopyTo(sendBuffer);
                new Random().NextBytes(sendBuffer[clientStream.TransportHeader.Length..]);
                sendBuffers.Span[0] = sendBuffer;
            }
            else
            {
                sendBuffers.Span[0] = clientStream.TransportHeader.ToArray();
                for (int i = 1; i < bufferCount; ++i)
                {
                    buffer = new byte[sendSize / bufferCount];
                    new Random().NextBytes(buffer);
                    sendBuffers.Span[i] = buffer;
                }
            }
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            _ = clientStream.SendAsync(sendBuffers, false, default).AsTask();

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            _ = ServerConnection.AcceptStreamAsync(default).AsTask();

            int segment = 0;
            int offset = 0;
            buffer = new byte[recvSize];
            ReadOnlyMemory<byte> receiveBuffer = ReadOnlyMemory<byte>.Empty;
            while (segment < bufferCount)
            {
                ReadOnlyMemory<byte> sendBuffer = sendBuffers.Span[segment];
                if (clientStream.TransportHeader.Length > 0 && segment == 0 && offset == 0)
                {
                    // Don't compare the transport header from the buffer since it's never returned by ReceiveAsync
                    if (sendBuffer.Length == clientStream.TransportHeader.Length)
                    {
                        sendBuffer = sendBuffers.Span[++segment];
                    }
                    else
                    {
                        offset = clientStream.TransportHeader.Length;
                    }
                }

                if (receiveBuffer.Length == 0)
                {
                    int count = await serverStream.ReceiveAsync(buffer, default);
                    receiveBuffer = buffer.AsMemory()[0..count];
                }

                if (receiveBuffer.Length < (sendBuffer.Length - offset))
                {
                    // Received buffer is smaller than the data from the send buffer segment
                    Assert.That(receiveBuffer.ToArray(),
                                Is.EqualTo(sendBuffer[offset..(offset + receiveBuffer.Length)].ToArray()));
                    offset += receiveBuffer.Length;
                    if (offset == sendBuffer.Length)
                    {
                        offset = 0;
                        ++segment;
                    }
                    receiveBuffer = Memory<byte>.Empty;
                }
                else
                {
                    // Received buffer is larger or equal than from the send buffer segment
                    int length = sendBuffer.Length - offset;
                    Assert.That(receiveBuffer[0..length].ToArray(), Is.EqualTo(sendBuffer[offset..].ToArray()));
                    ++segment;
                    offset = 0;
                    receiveBuffer = receiveBuffer[length..];
                }
            }
        }

        [TestCase(64)]
        [TestCase(1024)]
        [TestCase(32 * 1024)]
        [TestCase(128 * 1024)]
        [TestCase(512 * 1024)]
        public async Task Stream_SendReceiveRequestAsync(int size)
        {
            var request = new OutgoingRequest(Protocol.Ice2, path: "/dummy", operation: "op")
            {
                Payload = new ReadOnlyMemory<byte>[] { new byte[size] },
                PayloadEncoding = Encoding.Ice20,
            };
            ValueTask receiveTask = PerformReceiveAsync();

            RpcStream stream = ClientConnection.CreateStream(false);
            await stream.SendRequestFrameAsync(request);

            await receiveTask;

            async ValueTask PerformReceiveAsync()
            {
                RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
                ValueTask<RpcStream> _ = ServerConnection.AcceptStreamAsync(default);
                await serverStream.ReceiveRequestFrameAsync();
            }
        }

        [Test]
        public void Stream_SendRequestAsync_Cancellation()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendRequestFrameAsync(DummyRequest, source.Token));
        }

        [Test]
        public async Task Stream_SendResponse_CancellationAsync()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();

            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await serverStream.SendResponseFrameAsync(DummyResponse, source.Token));
        }

        [Test]
        public void Stream_ReceiveRequest_Cancellation()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveRequestFrameAsync(source.Token));
        }

        [Test]
        public async Task Stream_ReceiveResponse_Cancellation1Async()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveResponseFrameAsync(DummyRequest, source.Token));
        }

        [Test]
        public async Task Stream_ReceiveResponse_Cancellation2Async()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            await stream.SendRequestFrameAsync(DummyRequest);

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();
            _ = ServerConnection.AcceptStreamAsync(default).AsTask();

            var dispatchCanceled = new TaskCompletionSource();
            serverStream.CancelDispatchSource!.Token.Register(() => dispatchCanceled.SetResult());

            using var source = new CancellationTokenSource();
            ValueTask<IncomingResponse> responseTask = stream.ReceiveResponseFrameAsync(DummyRequest, source.Token);
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await responseTask);

            if (ConnectionType != MultiStreamConnectionType.Ice1)
            {
                stream.Abort(RpcStreamError.InvocationCanceled);

                // Ensure the stream cancel dispatch source is canceled
                await dispatchCanceled.Task;
            }
        }

        [TestCase(256, 256)]
        [TestCase(1024, 256)]
        [TestCase(256, 1024)]
        [TestCase(64 * 1024, 384)]
        [TestCase(384, 64 * 1024)]
        [TestCase(1024 * 1024, 1024)]
        [TestCase(1024, 1024 * 1024)]
        public async Task Stream_StreamReaderWriterAsync(int sendSize, int recvSize)
        {
            if (ConnectionType == MultiStreamConnectionType.Ice1)
            {
                // Not supported with Ice1
                return;
            }

            Task<RpcStream> serverAcceptStream = AcceptServerStreamAsync();

            RpcStream stream = ClientConnection.CreateStream(true);
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            _ = stream.SendAsync(CreateSendBuffer(stream, 1), false, default).AsTask();

            RpcStream serverStream = await serverAcceptStream;

            byte[] sendBuffer = new byte[sendSize];
            new Random().NextBytes(sendBuffer);

            IStreamParamSender writer = new ByteStreamParamSender(new MemoryStream(sendBuffer));
            _ = Task.Run(() => writer.SendAsync(stream, null), CancellationToken.None);

            byte[] receiveBuffer = new byte[recvSize];
            Stream receiveStream = new StreamParamReceiver(serverStream, null).ToByteStream();

            int offset = 0;
            while (offset < sendSize)
            {
                int received = await receiveStream.ReadAsync(receiveBuffer);
                Assert.That(receiveBuffer[0..received], Is.EqualTo(sendBuffer[offset..(offset + received)]));
                offset += received;
            }

            async Task<RpcStream> AcceptServerStreamAsync()
            {
                RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);

                // Continue reading from the server connection and receive the byte sent over the client stream.
                _ = ServerConnection.AcceptStreamAsync(default).AsTask();

                int received = await serverStream.ReceiveAsync(new byte[256], default);
                Assert.That(received, Is.EqualTo(1));
                return serverStream;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Stream_StreamReaderWriterCancelationAsync(bool cancelClientSide)
        {
            if (ConnectionType == MultiStreamConnectionType.Ice1)
            {
                // Not supported with Ice1
                return;
            }

            Task<RpcStream> serverAcceptStream = AcceptServerStreamAsync();

            RpcStream stream = ClientConnection.CreateStream(true);
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            _ = stream.SendAsync(CreateSendBuffer(stream, 1), false, default).AsTask();

            RpcStream serverStream = await serverAcceptStream;

            var sendStream = new TestMemoryStream(new byte[100]);

            IStreamParamSender writer = new ByteStreamParamSender(sendStream);
            _ = Task.Run(() => writer.SendAsync(stream, null), CancellationToken.None);

            byte[] readBuffer = new byte[100];
            Stream receiveStream = new StreamParamReceiver(serverStream, null).ToByteStream();

            ValueTask<int> readTask = receiveStream.ReadAsync(readBuffer);

            Assert.That(readTask.IsCompleted, Is.False);
            sendStream.Semaphore.Release();
            Assert.That(await readTask, Is.EqualTo(100));

            ValueTask<int> readTask2 = receiveStream.ReadAsync(readBuffer);
            await Task.Delay(100);
            Assert.That(readTask2.IsCompleted, Is.False);

            if (cancelClientSide)
            {
                sendStream.Exception = new InvalidOperationException();
                sendStream.Semaphore.Release();
            }
            else
            {
                receiveStream.Dispose();
            }

            // Make sure that the read fails with an IOException, the send stream should be disposed as well.
            IOException? ex = Assert.ThrowsAsync<IOException>(async () => await readTask2);
            if (cancelClientSide)
            {
                Assert.That(ex!.Message, Is.EqualTo("streaming canceled by the writer"));
            }
            else
            {
                Assert.That(ex!.Message, Is.EqualTo("streaming canceled by the reader"));

                // Release the semaphore, the stream should be disposed once the stop sending frame is received.
                sendStream.Semaphore.Release(10000);
            }
            await sendStream.Completed.Task;

            // Ensure the server can still send data to the client even if the client can no longer send data to
            // the server.
            await serverStream.SendAsync(CreateSendBuffer(serverStream, 1), false, default);
            await stream.ReceiveAsync(new byte[1], default);

            async Task<RpcStream> AcceptServerStreamAsync()
            {
                RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);

                // Continue reading from the server connection and receive the byte sent over the client stream.
                _ = ServerConnection.AcceptStreamAsync(default).AsTask();

                int received = await serverStream.ReceiveAsync(new byte[256], default);
                Assert.That(received, Is.EqualTo(1));
                return serverStream;
            }
        }

        [Test]
        public async Task Stream_StreamReaderWriterCompressorAsync()
        {
            if (ConnectionType == MultiStreamConnectionType.Ice1)
            {
                // Not supported with Ice1
                return;
            }

            RpcStream clientStream = ClientConnection.CreateStream(true);
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            _ = clientStream.SendAsync(CreateSendBuffer(clientStream, 1), false, default).AsTask();

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            _ = ServerConnection.AcceptStreamAsync(default).AsTask();
            await serverStream.ReceiveAsync(new byte[1], default);

            byte[] buffer = new byte[10000];
            var sendStream = new MemoryStream(buffer);

            bool compressorCalled = false;
            bool decompressorCalled = false;

            IStreamParamSender writer = new ByteStreamParamSender(sendStream);
            _ = Task.Run(() =>
            {
                writer.SendAsync(
                    clientStream,
                    outputStream =>
                    {
                        compressorCalled = true;
                        return (CompressionFormat.Deflate,
                                new DeflateStream(outputStream, System.IO.Compression.CompressionLevel.SmallestSize));
                    });
            },
            CancellationToken.None);

            Stream receiveStream = new StreamParamReceiver(
                serverStream,
                (compressionFormat, inputStream) =>
                {
                    decompressorCalled = true;
                    return new DeflateStream(inputStream, CompressionMode.Decompress);
                }
                ).ToByteStream();

            byte[] readBuffer = new byte[10000];
            int offset = 0;
            int received;
            while ((received = await receiveStream.ReadAsync(readBuffer)) != 0)
            {
                offset += received;
            }

            Assert.That(compressorCalled, Is.True);
            Assert.That(decompressorCalled, Is.True);
            Assert.That(offset, Is.EqualTo(buffer.Length));
            Assert.That(readBuffer, Is.EqualTo(buffer));
        }

        private static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateSendBuffer(RpcStream stream, int length)
        {
            byte[] buffer = new byte[stream.TransportHeader.Length + length];
            stream.TransportHeader.CopyTo(buffer);
            return new ReadOnlyMemory<byte>[] { buffer };
        }

        private class TestMemoryStream : MemoryStream
        {
            public Exception? Exception { get; set; }
            public TaskCompletionSource Completed = new();
            public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(0);

            private int _received = 100;

            public TestMemoryStream(byte[] buffer)
                : base(buffer)
            {
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
            {
                if (_received == 100)
                {
                    await Semaphore.WaitAsync(cancel);
                    if (Exception != null)
                    {
                        throw Exception;
                    }
                    Seek(0, SeekOrigin.Begin);
                    _received = 0;
                }
                int received = await base.ReadAsync(buffer, cancel);
                _received += received;
                return received;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                Semaphore.Dispose();
                Completed.SetResult();
            }
        }
    }
}
