// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.IO.Compression;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    public class MultiplexedStreamTests : MultiplexedStreamFactoryBaseTest
    {
        [SetUp]
        public Task SetUp() => SetUpConnectionsAsync();

        [TearDown]
        public void TearDown() => TearDownConnections();

        [TestCase(StreamError.DispatchCanceled)]
        [TestCase(StreamError.InvocationCanceled)]
        [TestCase((StreamError)10)]
        public async Task MultiplexedStream_Abort(StreamError errorCode)
        {
            Task<(int, IMultiplexedStream)> serverTask = ReceiveAsync();

            // Create client stream and send one byte.
            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(true);
            await clientStream.WriteAsync(CreateSendPayload(clientStream, 1), false, default);

            (int received, IMultiplexedStream serverStream) = await serverTask;

            Assert.That(received, Is.EqualTo(1));
            var dispatchCanceled = new TaskCompletionSource();
            serverStream.ShutdownAction = () => dispatchCanceled.SetResult();

            // Abort the stream
            clientStream.Abort(errorCode);

            // Ensure that receive on the server connection raises RpcStreamAbortedException
            StreamAbortedException? ex = Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.ReadAsync(new byte[1], default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));

            await dispatchCanceled.Task;

            // Ensure we can still create a new stream after the cancellation
            IMultiplexedStream clientStream2 = ClientMultiplexedStreamFactory.CreateStream(true);
            await clientStream2.WriteAsync(CreateSendPayload(clientStream2, 1), true, default);

            async Task<(int, IMultiplexedStream)> ReceiveAsync()
            {
                IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);

                // Continue reading from the server connection and receive the byte sent over the client stream.
                _ = ServerMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();

                return (await serverStream.ReadAsync(new byte[256], default), serverStream);
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
        public async Task MultiplexedStream_StreamSendReceiveAsync(bool flowControl, int bufferCount, int sendSize, int recvSize)
        {
            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(true);
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
            _ = ClientMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();
            _ = clientStream.WriteAsync(sendBuffers, false, default).AsTask();

            IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);
            _ = ServerMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();

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
                    int count = await serverStream.ReadAsync(buffer, default);
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

        [Test]
        public void MultiplexedStream_SendAsync_Cancellation()
        {
            IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.WriteAsync(CreateSendPayload(stream), true, source.Token));
        }

        [Test]
        public void MultiplexedStream_ReceiveAsync_Cancellation()
        {
            IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReadAsync(CreateReceivePayload(), source.Token));
        }

        [Test]
        public async Task MultiplexedStream_ReceiveAsync_Cancellation2Async()
        {
            IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(true);
            _ = ClientMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();
            await stream.WriteAsync(CreateSendPayload(stream), true, default);

            IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);
            await serverStream.ReadAsync(CreateReceivePayload(), default);
            _ = ServerMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();

            var dispatchCanceled = new TaskCompletionSource();
            serverStream.ShutdownAction = () => dispatchCanceled.SetResult();

            using var source = new CancellationTokenSource();
            ValueTask<int> receiveTask = stream.ReadAsync(CreateReceivePayload(), source.Token);
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [TestCase(256, 256)]
        [TestCase(1024, 256)]
        [TestCase(256, 1024)]
        [TestCase(64 * 1024, 384)]
        [TestCase(384, 64 * 1024)]
        [TestCase(1024 * 1024, 1024)]
        [TestCase(1024, 1024 * 1024)]
        public async Task MultiplexedStream_StreamReaderWriterAsync(int sendSize, int recvSize)
        {
            Task<IMultiplexedStream> serverAcceptStream = AcceptServerStreamAsync();

            IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(true);
            _ = ClientMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();
            _ = stream.WriteAsync(CreateSendPayload(stream, 1), false, default).AsTask();

            IMultiplexedStream serverStream = await serverAcceptStream;

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

            async Task<IMultiplexedStream> AcceptServerStreamAsync()
            {
                IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);

                // Continue reading from the server connection and receive the byte sent over the client stream.
                _ = ServerMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();

                int received = await serverStream.ReadAsync(new byte[256], default);
                Assert.That(received, Is.EqualTo(1));
                return serverStream;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedStream_StreamReaderWriterCancelationAsync(bool cancelClientSide)
        {
            Task<IMultiplexedStream> serverAcceptStream = AcceptServerStreamAsync();

            IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(true);
            _ = ClientMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();
            _ = stream.WriteAsync(CreateSendPayload(stream, 1), false, default).AsTask();

            IMultiplexedStream serverStream = await serverAcceptStream;

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
                await receiveStream.DisposeAsync();
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
            await serverStream.WriteAsync(CreateSendPayload(serverStream, 1), false, default);
            await stream.ReadAsync(new byte[1], default);

            async Task<IMultiplexedStream> AcceptServerStreamAsync()
            {
                IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);

                // Continue reading from the server connection and receive the byte sent over the client stream.
                _ = ServerMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();

                int received = await serverStream.ReadAsync(new byte[256], default);
                Assert.That(received, Is.EqualTo(1));
                return serverStream;
            }
        }

        [Test]
        public async Task MultiplexedStream_StreamReaderWriterCompressorAsync()
        {
            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(true);
            _ = ClientMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();
            _ = clientStream.WriteAsync(CreateSendPayload(clientStream, 1), false, default).AsTask();

            IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);
            _ = ServerMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();
            await serverStream.ReadAsync(new byte[1], default);

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
