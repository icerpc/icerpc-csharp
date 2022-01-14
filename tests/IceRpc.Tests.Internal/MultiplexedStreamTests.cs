// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    public class MultiplexedStreamTests
    {
        [TestCase(false, 145, false)]
        [TestCase(true, 145, false)]
        [TestCase(false, 15, false)]
        [TestCase(true, 15, false)]
        [TestCase(false, 3, true)]
        public async Task MultiplexedStream_Abort(bool abortWrite, byte errorCode, bool endStream)
        {
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> serverTask =
                serviceProvider.GetMultiplexedServerConnectionAsync();
            await using IMultiplexedNetworkConnection clientConnection =
                await serviceProvider.GetMultiplexedClientConnectionAsync();
            await using IMultiplexedNetworkConnection serverConnection = await serverTask;

            (IMultiplexedStream serverStream, IMultiplexedStream clientStream) = await GetServerClientStreamsAsync(
                serverConnection,
                clientConnection);

            if (abortWrite)
            {
                // Abort the write side of the client stream.
                await clientStream.Output.CompleteAsync(new MultiplexedStreamAbortedException(errorCode));
            }
            else
            {
                // Abort the read side of the server stream.
                await serverStream.Input.CompleteAsync(new MultiplexedStreamAbortedException(errorCode));
            }

            // Wait for the peer to receive the StreamStopSending/StreamReset frame.
            await Task.Delay(500);

            MultiplexedStreamAbortedException? ex = null;
            if (abortWrite)
            {
                // Ensure that ReadAsync raises MultiplexedStreamAbortedException
                ex = Assert.CatchAsync<MultiplexedStreamAbortedException>(
                    async () => await serverStream.Input.ReadAsync());
            }
            else
            {
                // Ensure that WriteAsync raises MultiplexedStreamAbortedException
                ex = Assert.ThrowsAsync<MultiplexedStreamAbortedException>(
                    async () => await clientStream.Output.WriteAsync(new byte[1], endStream, default));
            }

            // Check the code
            Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));

            // Complete the other side to shutdown the streams.
            if (abortWrite)
            {
                await serverStream.Output.CompleteAsync();
            }
            else
            {
                await clientStream.Input.CompleteAsync();
            }

            // Ensure streams are shutdown.
            await serverStream.WaitForShutdownAsync(default);
            await clientStream.WaitForShutdownAsync(default);

            // Ensure we can still create a new stream after the abort of the previous stream.
            _ = await GetServerClientStreamsAsync(serverConnection, clientConnection);
        }

        [TestCase(1, 256)]
        [TestCase(4, 256)]
        [TestCase(1, 1024)]
        [TestCase(4, 1024)]
        [TestCase(1, 1024 * 1024)]
        [TestCase(2, 1024 * 1024)]
        [TestCase(10, 1024 * 1024)]
        public async Task MultiplexedStream_StreamSendReceiveAsync(int segmentCount, int segmentSize)
        {
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> serverTask =
                serviceProvider.GetMultiplexedServerConnectionAsync();
            await using IMultiplexedNetworkConnection clientConnection =
                await serviceProvider.GetMultiplexedClientConnectionAsync();
            await using IMultiplexedNetworkConnection serverConnection = await serverTask;

            IMultiplexedStream clientStream = clientConnection.CreateStream(true);
            byte[] sendBuffer = new byte[segmentSize * segmentCount];
            new Random().NextBytes(sendBuffer);
            int sendOffset = 0;
            for (int i = 0; i < segmentCount; ++i)
            {
                sendBuffer[sendOffset..(sendOffset + segmentSize)].CopyTo(clientStream.Output.GetMemory(segmentSize));
                clientStream.Output.Advance(segmentSize);
                sendOffset += segmentSize;
            }

            _ = Task.Run(async () => await clientStream.Output.FlushAsync());

            IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);

            sendOffset = 0;
            while (sendOffset < sendBuffer.Length)
            {
                ReadResult readResult = await serverStream.Input.ReadAsync();
                foreach (ReadOnlyMemory<byte> memory in readResult.Buffer)
                {
                    ReadOnlyMemory<byte> expected = sendBuffer[sendOffset..(sendOffset + memory.Length)].AsMemory();
                    Assert.That(memory.Span.SequenceEqual(expected.Span), Is.True);
                    sendOffset += memory.Length;
                }
                serverStream.Input.AdvanceTo(readResult.Buffer.End);
            }
        }

        [Test]
        public async Task MultiplexedStream_SendAsync_Cancellation()
        {
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> serverTask =
                serviceProvider.GetMultiplexedServerConnectionAsync();
            await using IMultiplexedNetworkConnection clientConnection =
                await serviceProvider.GetMultiplexedClientConnectionAsync();
            await using IMultiplexedNetworkConnection serverConnection = await serverTask;

            IMultiplexedStream stream = clientConnection.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.Output.WriteAsync(new byte[10], true, source.Token));
        }

        [Test]
        public async Task MultiplexedStream_ReceiveAsync_Cancellation()
        {
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> serverTask =
                serviceProvider.GetMultiplexedServerConnectionAsync();
            await using IMultiplexedNetworkConnection clientConnection =
                await serviceProvider.GetMultiplexedClientConnectionAsync();
            await using IMultiplexedNetworkConnection serverConnection = await serverTask;

            IMultiplexedStream stream = clientConnection.CreateStream(true);
            await stream.Output.WriteAsync(new byte[10], true, default); // start the stream

            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await stream.Input.ReadAsync(source.Token));
        }

        [Test]
        public async Task MultiplexedStream_ReceiveAsync_Cancellation2Async()
        {
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> serverTask =
                serviceProvider.GetMultiplexedServerConnectionAsync();
            await using IMultiplexedNetworkConnection clientConnection =
                await serviceProvider.GetMultiplexedClientConnectionAsync();
            await using IMultiplexedNetworkConnection serverConnection = await serverTask;

            (IMultiplexedStream serverStream, IMultiplexedStream clientStream) = await GetServerClientStreamsAsync(
                serverConnection,
                clientConnection);

            using var source = new CancellationTokenSource();
            ValueTask<ReadResult> receiveTask = clientStream.Input.ReadAsync(source.Token);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);

            // Ensure stream is shutdown.
            // TODO: XXX shutdown read async completion trigger stream completion?
            // await serverStream.WaitForShutdownAsync(default);
        }

        /*  TODO: reenable with updated IMultiplexedStream API

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedStream_StreamReaderWriterCancelationAsync(bool cancelClientSide)
        {
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> serverTask =
                serviceProvider.GetMultiplexedServerConnectionAsync();
            await using IMultiplexedNetworkConnection clientConnection =
                await serviceProvider.GetMultiplexedClientConnectionAsync();
            await using IMultiplexedNetworkConnection serverConnection = await serverTask;

            Task<IMultiplexedStream> serverAcceptStream = AcceptServerStreamAsync(serverConnection);

            IMultiplexedStream stream = clientConnection.CreateStream(true);
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
        */

        private static async Task<(IMultiplexedStream, IMultiplexedStream)> GetServerClientStreamsAsync(
            IMultiplexedNetworkConnection serverConnection,
            IMultiplexedNetworkConnection clientConnection)
        {
            ValueTask<IMultiplexedStream> serverStreamTask = serverConnection.AcceptStreamAsync(default);

            IMultiplexedStream clientStream = clientConnection.CreateStream(true);
            _ = clientStream.Output.WriteAsync(new byte[1], false, default).AsTask();

            IMultiplexedStream serverStream = await serverStreamTask;
            ReadResult result = await serverStream.Input.ReadAsync();
            Assert.That(result.Buffer.Length, Is.EqualTo(1));

            return (serverStream, clientStream);
        }

    }
}
