// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class MultiplexedStreamTests
    {
        private ServiceProvider? _serviceProvider;
        private IMultiplexedNetworkConnection? _clientConnection;
        private IMultiplexedNetworkConnection? _serverConnection;
        private IMultiplexedStream? _clientStream;
        private IMultiplexedStream? _serverStream;
        private IMultiplexedStream ClientStream => _clientStream!;
        private IMultiplexedStream ServerStream => _serverStream!;

        [SetUp]
        public async Task Setup()
        {
            _serviceProvider = new InternalTestServiceCollection().BuildServiceProvider();
            Task<IMultiplexedNetworkConnection> serverTask = _serviceProvider.GetMultiplexedServerConnectionAsync();
            _clientConnection = await _serviceProvider.GetMultiplexedClientConnectionAsync();
            _serverConnection = await serverTask;

            ValueTask<IMultiplexedStream> ServerStreamTask = _serverConnection!.AcceptStreamAsync(default);

            _clientStream = _clientConnection!.CreateStream(true);
            _ = _clientStream.Output.WriteAsync(new byte[1], false, default).AsTask();

            _serverStream = await ServerStreamTask;
            ReadResult result = await ServerStream.Input.ReadAsync();
            Assert.That(result.Buffer.Length, Is.EqualTo(1));
            _serverStream.Input.AdvanceTo(result.Buffer.End);
        }

        [TearDown]
        public async Task TearDown()
        {
            await _clientConnection!.DisposeAsync();
            await _serverConnection!.DisposeAsync();
            await _serviceProvider!.DisposeAsync();
        }

        [TestCase(true, null, false)]
        [TestCase(true, 15, false)]
        [TestCase(true, 145, false)]
        [TestCase(false, null, false)]
        [TestCase(false, null, true)]
        [TestCase(false, 15, false)]
        [TestCase(false, 145, false)]
        [TestCase(false, 145, true)]
        public async Task MultiplexedStream_Abort(bool abortWrite, byte? errorCode, bool endStream)
        {
            Exception? exception =
                errorCode == null ? null :
                errorCode == 200 ? new InvalidOperationException() :
                new MultiplexedStreamAbortedException(errorCode.Value);

            if (abortWrite)
            {
                await ClientStream.Output.CompleteAsync(exception);
            }
            else
            {
                await ServerStream.Input.CompleteAsync(exception);
            }

            // Wait for the peer to receive the StreamStopSending/StreamReset frame.
            await Task.Delay(500);

            if (exception == null)
            {
                if (abortWrite)
                {
                    ReadResult readResult = await ServerStream.Input.ReadAsync();
                    Assert.That(readResult.IsCompleted);
                }
                else
                {
                    FlushResult flushResult = await ClientStream.Output.WriteAsync(new byte[1], endStream, default);
                    Assert.That(flushResult.IsCompleted);
                }
            }
            else
            {
                // Ensure that WriteAsync raises MultiplexedStreamAbortedException
                MultiplexedStreamAbortedException? ex = null;
                if (abortWrite)
                {
                    ex = Assert.CatchAsync<MultiplexedStreamAbortedException>(
                        async () => await ServerStream.Input.ReadAsync());
                }
                else
                {
                    ex = Assert.ThrowsAsync<MultiplexedStreamAbortedException>(
                        async () => await ClientStream.Output.WriteAsync(new byte[1], endStream, default));
                }

                // Check the code
                Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode == 200 ? 1 : errorCode));
            }

            // Complete the pipe readers/writers to shutdown the stream.
            if (abortWrite)
            {
                await ServerStream.Input.CompleteAsync();
            }
            else
            {
                await ClientStream.Output.CompleteAsync();
            }
            await ClientStream.Input.CompleteAsync();
            await ServerStream.Output.CompleteAsync();

            // Ensure streams are shutdown.
            await ServerStream.WaitForShutdownAsync(default);
            await ClientStream.WaitForShutdownAsync(default);
        }

        [Test]
        public void MultiplexedStream_AbortWithUnflushedBytes()
        {
            Memory<byte> buffer = ClientStream.Output.GetMemory();
            ClientStream.Output.Advance(10);
            Assert.Throws<NotSupportedException>(() => ClientStream.Output.Complete());

        }

        [Test]
        public async Task MultiplexedStream_ConnectionDisposeAsync()
        {
            // Connection dispose aborts the streams which completes the reader/writer.
            await _clientConnection!.DisposeAsync();

            // Can't read/write once the writer/reader is completed.
            Assert.ThrowsAsync<InvalidOperationException>(() => ClientStream.Input.ReadAsync().AsTask());
            Assert.ThrowsAsync<InvalidOperationException>(
                () => ClientStream.Output.WriteAsync(ReadOnlyMemory<byte>.Empty).AsTask());

            await _serverConnection!.DisposeAsync();

            Assert.ThrowsAsync<InvalidOperationException>(() => ServerStream.Input.ReadAsync().AsTask());
            Assert.ThrowsAsync<InvalidOperationException>(
                () => ServerStream.Output.WriteAsync(ReadOnlyMemory<byte>.Empty).AsTask());
        }

        [TestCase(1, 256, false)]
        [TestCase(1, 256, true)]
        [TestCase(32, 256, false)]
        [TestCase(32, 256, true)]
        [TestCase(1, 1024, false)]
        [TestCase(4, 1024, false)]
        [TestCase(256, 1024, false)]
        [TestCase(1, 1024 * 1024, false)]
        [TestCase(1, 1024 * 1024, true)]
        [TestCase(4, 1024 * 1024, false)]
        [TestCase(4, 1024 * 1024, true)]
        public async Task MultiplexedStream_StreamSendReceiveAsync(int segmentCount, int segmentSize, bool consume)
        {
            await ClientStream.Input.CompleteAsync();
            await ServerStream.Output.CompleteAsync();

            byte[] sendBuffer = new byte[segmentSize * segmentCount];
            new Random().NextBytes(sendBuffer);
            int sendOffset = 0;
            for (int i = 0; i < segmentCount; ++i)
            {
                sendBuffer[sendOffset..(sendOffset + segmentSize)].CopyTo(ClientStream.Output.GetMemory(segmentSize));
                ClientStream.Output.Advance(segmentSize);
                sendOffset += segmentSize;
            }

            _ = Task.Run(async () => await ClientStream.Output.FlushAsync());

            sendOffset = 0;
            while (sendOffset < sendBuffer.Length)
            {
                ReadResult readResult = await ServerStream.Input.ReadAsync();
                foreach (ReadOnlyMemory<byte> memory in readResult.Buffer)
                {
                    ReadOnlyMemory<byte> expected = sendBuffer[sendOffset..(sendOffset + memory.Length)].AsMemory();
                    Assert.That(memory.Span.SequenceEqual(expected.Span), Is.True);
                    sendOffset += memory.Length;
                }

                if (consume)
                {
                    ServerStream.Input.AdvanceTo(readResult.Buffer.End);
                }
                else
                {
                    ServerStream.Input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    if (sendOffset < sendBuffer.Length)
                    {
                        // Always compare from the start of the send buffer since we don't consume the read buffer.
                        sendOffset = 0;
                    }
                }

                if (readResult.IsCompleted)
                {
                    Assert.That(sendOffset, Is.EqualTo(sendBuffer.Length));
                }
            }

            await ServerStream.Input.CompleteAsync().ConfigureAwait(false);
            await ClientStream.Output.CompleteAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task MultiplexedStream_StreamCompleteOnFrameRead()
        {
            await ClientStream.Input.CompleteAsync();
            await ServerStream.Output.CompleteAsync();

            // Continuously push data to the client stream.
            _ = FillOutputPipeAsync();

            // Read first chunk to make sure we start receiving data.
            ReadResult result = await ServerStream.Input.ReadAsync();
            ServerStream.Input.AdvanceTo(result.Buffer.End);

            // Complete the server input pipe to ensure that it doesn't cause any issues while concurrently receiving
            // frames from the client. Because the server output pipe is already completed, the stream will shutdown.
            await ServerStream.Input.CompleteAsync();

            // Both stream should be shutdown at this point.
            await ServerStream.WaitForShutdownAsync(default);
            await ClientStream.WaitForShutdownAsync(default);

            // Ensure the connection can still accept streams.
            ValueTask<IMultiplexedStream> serverTask = _serverConnection!.AcceptStreamAsync(default);
            IMultiplexedStream stream = _clientConnection!.CreateStream(false);
            await stream.Output.WriteAsync(new byte[10], default);
            await serverTask;

            async Task FillOutputPipeAsync()
            {
                FlushResult result = default;
                while (!result.IsCompleted)
                {
                    result = await ClientStream.Output.WriteAsync(new byte[1024 * 1024]);
                }
                Assert.That(result.IsCanceled, Is.False);
                await ClientStream.Output.CompleteAsync();
            }
        }

        [Test]
        public void MultiplexedStream_SendAsync_Cancellation()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientStream.Output.WriteAsync(new byte[10], true, source.Token));
        }

        [Test]
        public async Task MultiplexedStream_ReceiveAsync_Cancellation()
        {
            await ClientStream.Output.WriteAsync(new byte[10], true, default); // start the stream

            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await ClientStream.Input.ReadAsync(source.Token));
        }

        [Test]
        public void MultiplexedStream_ReceiveAsync_Cancellation2Async()
        {
            using var source = new CancellationTokenSource();
            ValueTask<ReadResult> receiveTask = ClientStream.Input.ReadAsync(source.Token);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }
    }
}
