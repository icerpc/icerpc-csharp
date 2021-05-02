// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [Timeout(10000)]
    [TestFixture(MultiStreamSocketType.Slic)]
    [TestFixture(MultiStreamSocketType.Coloc)]
    [TestFixture(MultiStreamSocketType.Ice1)]
    public class MultiStreamSocketStreamTests : MultiStreamSocketBaseTest
    {
        public MultiStreamSocketStreamTests(MultiStreamSocketType socketType)
            : base(socketType)
        {
        }

        [SetUp]
        public Task SetUp() => SetUpSocketsAsync();

        [TearDown]
        public void TearDown() => TearDownSockets();

        [TestCase(64)]
        [TestCase(1024)]
        [TestCase(32 * 1024)]
        [TestCase(128 * 1024)]
        [TestCase(512 * 1024)]
        public async Task MultiStreamSocketStream_SendReceiveRequestAsync(int size)
        {
            var requestPayload = Payload.FromArgs(Proxy,
                                                  new byte[size],
                                                  (OutputStream ostr, ReadOnlyMemory<byte> value) =>
                                                    ostr.WriteSequence(value.Span));

            var request = new OutgoingRequest(Proxy, "op", requestPayload, DateTime.MaxValue);
            ValueTask receiveTask = PerformReceiveAsync();

            SocketStream stream = ClientSocket.CreateStream(false);
            await stream.SendRequestFrameAsync(request);
            stream.Release();

            await receiveTask;

            async ValueTask PerformReceiveAsync()
            {
                SocketStream serverStream = await ServerSocket.AcceptStreamAsync(default);
                ValueTask<SocketStream> _ = ServerSocket.AcceptStreamAsync(default);
                await serverStream.ReceiveRequestFrameAsync();
                serverStream.Release();
            }
        }

        [Test]
        public async Task MultiStreamSocketStream_SendRequest_CancellationAsync()
        {
            SocketStream stream = ClientSocket.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendRequestFrameAsync(DummyRequest, source.Token));
            stream.Release();

            if (SocketType == MultiStreamSocketType.Slic)
            {
                // With Slic, large frames are sent with multiple packets. Here we ensure that cancelling the sending
                // while the packets are being sent works.

                var requestPayload = Payload.FromArgs(Proxy,
                                                      new byte[256 * 1024],
                                                      (OutputStream ostr, ReadOnlyMemory<byte> value) =>
                                                        ostr.WriteSequence(value.Span));

                var request = new OutgoingRequest(Proxy, "op", requestPayload, DateTime.MaxValue);

                int requestCount = 0;
                while (true)
                {
                    stream = ClientSocket.CreateStream(true);
                    try
                    {
                        using var source2 = new CancellationTokenSource();
                        source2.CancelAfter(500);
                        ValueTask sendTask = stream.SendRequestFrameAsync(request, source2.Token);
                        await sendTask;
                        requestCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    finally
                    {
                        stream.Release();
                    }
                }

                await ReceiveRequestsAsync(requestCount);
            }

            // Ensure we can still send a request after the cancellation
            stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            stream.Release();

            async Task ReceiveRequestsAsync(int requestCount)
            {
                if (requestCount == 0)
                {
                    try
                    {
                        using var source = new CancellationTokenSource(500);
                        SocketStream serverStream = await ServerSocket.AcceptStreamAsync(source.Token);
                        _ = ServerSocket.AcceptStreamAsync(default).AsTask();
                        Assert.CatchAsync<TransportException>(
                            async () => await serverStream.ReceiveRequestFrameAsync());
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                else
                {
                    SocketStream serverStream = await ServerSocket.AcceptStreamAsync(default);
                    Task receiveNextRequestTask = ReceiveRequestsAsync(--requestCount);
                    _ = await serverStream.ReceiveRequestFrameAsync();
                    serverStream.Release();
                    await receiveNextRequestTask;
                }
            }
        }

        [Test]
        public async Task MultiStreamSocketStream_SendResponse_CancellationAsync()
        {
            SocketStream stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            SocketStream serverStream = await ServerSocket.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();

            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendResponseFrameAsync(GetResponseFrame(request), source.Token));

            stream.Release();
            serverStream.Release();
        }

        [Test]
        public void MultiStreamSocketStream_ReceiveRequest_Cancellation()
        {
            SocketStream stream = ClientSocket.CreateStream(false);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveRequestFrameAsync(source.Token));
            stream.Release();
        }

        [Test]
        public async Task MultiStreamSocketStream_ReceiveResponse_CancellationAsync()
        {
            SocketStream stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveResponseFrameAsync(source.Token));
            stream.Release();
        }
    }
}
