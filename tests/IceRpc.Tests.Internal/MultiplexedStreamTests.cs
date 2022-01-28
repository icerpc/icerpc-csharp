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
            MultiplexedStreamAbortedException? exception =
                errorCode == null ? null : new MultiplexedStreamAbortedException(errorCode.Value);
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
                Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));
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
        // [Log(LogAttributeLevel.Debug)]
        public async Task MultiplexedStream_StreamSendReceiveAsync(int segmentCount, int segmentSize, bool consume)
        {
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
