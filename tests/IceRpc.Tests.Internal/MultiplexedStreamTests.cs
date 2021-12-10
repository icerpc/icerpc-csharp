// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    public class MultiplexedStreamTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IMultiplexedNetworkConnection _serverConnection;
        private readonly IMultiplexedNetworkConnection _clientConnection;

        public MultiplexedStreamTests()
        {
            _serviceProvider = new NetworkConnectionTestServiceCollection().UseTcp().BuildServiceProvider();
            // Task<IMultiplexedNetworkConnection> serverTask = _serviceProvider.GetMultiplexedServerConnectionAsync();
            // _clientConnection = _serviceProvider.GetMultiplexedClientConnectionAsync().Result;
            // _serverConnection = serverTask.Result;
            _clientConnection = null!;
            _serverConnection = null!;
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            // await _clientConnection.DisposeAsync();
            // await _serverConnection.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }

        [TestCase(1)]
        [TestCase(4)]
        [Log(LogAttributeLevel.Trace)]
        public async Task MultiplexedStream_Abort(byte errorCode)
        {
            await using var serviceProvider = new NetworkConnectionTestServiceCollection().UseTcp().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> server1Task = serviceProvider.GetMultiplexedServerConnectionAsync();
            var _clientConnection = await serviceProvider.GetMultiplexedClientConnectionAsync();
            var _serverConnection = await server1Task;

            Task<(int, IMultiplexedStream)> serverTask = ReceiveAsync();

            // Create client stream and send one byte.
            IMultiplexedStream clientStream = _clientConnection.CreateStream(true);
            await clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, false, default);

            (int received, IMultiplexedStream serverStream) = await serverTask;

            Assert.That(received, Is.EqualTo(1));
            var dispatchCanceled = new TaskCompletionSource();
            serverStream.ShutdownAction = () => dispatchCanceled.SetResult();

            // Abort the stream
            clientStream.AbortRead(errorCode);
            clientStream.AbortWrite(errorCode);

            // Ensure that receive on the server connection raises RpcStreamAbortedException
            MultiplexedStreamAbortedException? ex = Assert.CatchAsync<MultiplexedStreamAbortedException>(
                async () => await serverStream.ReadAsync(new byte[1], default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));

            await dispatchCanceled.Task;

            // Ensure we can still create a new stream after the cancellation
            IMultiplexedStream clientStream2 = _clientConnection.CreateStream(true);
            await clientStream2.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, true, default);

            async Task<(int, IMultiplexedStream)> ReceiveAsync()
            {
                IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);
                return (await serverStream.ReadAsync(new byte[256], default), serverStream);
            }
        }

        [TestCase(1, 256, 256)]
        [TestCase(1, 1024, 256)]
        [TestCase(1, 256, 1024)]
        [TestCase(1, 1024 * 1024, 1024)]
        [TestCase(1, 1024, 1024 * 1024)]
        [TestCase(2, 1024 * 1024, 1024)]
        [TestCase(2, 1024, 1024 * 1024)]
        [TestCase(3, 1024 * 1024, 1024)]
        [TestCase(3, 1024, 1024 * 1024)]
        public async Task MultiplexedStream_StreamSendReceiveAsync(int bufferCount, int sendSize, int recvSize)
        {
            IMultiplexedStream clientStream = _clientConnection.CreateStream(true);
            Memory<ReadOnlyMemory<byte>> sendBuffers = new ReadOnlyMemory<byte>[bufferCount];
            byte[] buffer;
            if (bufferCount == 1)
            {
                var sendBuffer = new ArraySegment<byte>(new byte[sendSize]);
                new Random().NextBytes(sendBuffer);
                sendBuffers.Span[0] = sendBuffer;
            }
            else
            {
                for (int i = 0; i < bufferCount; ++i)
                {
                    buffer = new byte[sendSize / bufferCount];
                    new Random().NextBytes(buffer);
                    sendBuffers.Span[i] = buffer;
                }
            }
            _ = clientStream.WriteAsync(sendBuffers, false, default).AsTask();

            IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);

            int segment = 0;
            int offset = 0;
            buffer = new byte[recvSize];
            ReadOnlyMemory<byte> receiveBuffer = ReadOnlyMemory<byte>.Empty;
            while (segment < bufferCount)
            {
                ReadOnlyMemory<byte> sendBuffer = sendBuffers.Span[segment];

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
            IMultiplexedStream stream = _clientConnection.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, source.Token));
        }

        [Test]
        public void MultiplexedStream_ReceiveAsync_Cancellation()
        {
            IMultiplexedStream stream = _clientConnection.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReadAsync(new byte[10], source.Token));
        }

        [Test]
        public async Task MultiplexedStream_ReceiveAsync_Cancellation2Async()
        {
            IMultiplexedStream stream = _clientConnection.CreateStream(true);
            await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);

            IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);
            await serverStream.ReadAsync(new byte[10], default);

            var dispatchCanceled = new TaskCompletionSource();
            serverStream.ShutdownAction = () => dispatchCanceled.SetResult();

            using var source = new CancellationTokenSource();
            ValueTask<int> receiveTask = stream.ReadAsync(new byte[10], source.Token);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
            stream.AbortRead(0);
            Assert.DoesNotThrowAsync(async () => await dispatchCanceled.Task);
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

            IMultiplexedStream stream = _clientConnection.CreateStream(true);
            _ = stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, false, default).AsTask();

            IMultiplexedStream serverStream = await serverAcceptStream;

            byte[] sendBuffer = new byte[sendSize];
            new Random().NextBytes(sendBuffer);

            IStreamParamSender writer = new ByteStreamParamSender(new MemoryStream(sendBuffer));
            _ = Task.Run(() => writer.SendAsync(stream), CancellationToken.None);

            byte[] receiveBuffer = new byte[recvSize];
            Stream receiveStream = new StreamParamReceiver(serverStream).ToByteStream();

            int offset = 0;
            while (offset < sendSize)
            {
                int received = await receiveStream.ReadAsync(receiveBuffer);
                Assert.That(receiveBuffer[0..received], Is.EqualTo(sendBuffer[offset..(offset + received)]));
                offset += received;
            }

            async Task<IMultiplexedStream> AcceptServerStreamAsync()
            {
                IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);
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

            IMultiplexedStream stream = _clientConnection.CreateStream(true);
            _ = stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, false, default).AsTask();

            IMultiplexedStream serverStream = await serverAcceptStream;

            var sendStream = new TestMemoryStream(new byte[100]);

            IStreamParamSender writer = new ByteStreamParamSender(sendStream);
            _ = Task.Run(() => writer.SendAsync(stream), CancellationToken.None);

            byte[] readBuffer = new byte[100];
            Stream receiveStream = new StreamParamReceiver(serverStream).ToByteStream();

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

            if (cancelClientSide)
            {
                // Make sure that the read fails with an IOException.
                IOException? ex = Assert.ThrowsAsync<IOException>(async () => await readTask2);
                Assert.That(ex!.Message, Is.EqualTo("streaming canceled by the writer"));
            }
            else
            {
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await readTask2);

                // Release the semaphore, the stream should be disposed once the stop sending frame is received.
                sendStream.Semaphore.Release(10000);
            }
            await sendStream.Completed.Task;

            // Ensure the server can still send data to the client even if the client can no longer send data to
            // the server.
            await serverStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, false, default);
            await stream.ReadAsync(new byte[1], default);

            async Task<IMultiplexedStream> AcceptServerStreamAsync()
            {
                IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);
                int received = await serverStream.ReadAsync(new byte[256], default);
                Assert.That(received, Is.EqualTo(1));
                return serverStream;
            }
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
