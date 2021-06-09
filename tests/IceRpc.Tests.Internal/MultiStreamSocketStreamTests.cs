// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
            IList<ArraySegment<byte>> requestPayload = Payload.FromSingleArg(
                Proxy,
                new byte[size],
                (OutputStream ostr, ReadOnlyMemory<byte> value) => ostr.WriteSequence(value.Span));

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
        public void MultiStreamSocketStream_SendRequestAsync_Cancellation()
        {
            SocketStream stream = ClientSocket.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendRequestFrameAsync(DummyRequest, source.Token));
            stream.Release();
        }

        [TestCase(SocketStreamErrorCode.DispatchCanceled)]
        [TestCase(SocketStreamErrorCode.InvocationCanceled)]
        [TestCase((SocketStreamErrorCode)10)]
        public async Task MultiStreamSocketStream_Reset(SocketStreamErrorCode errorCode)
        {
            // SendAsync/ReceiveAsync is only supported with Slic
            if (SocketType != MultiStreamSocketType.Slic)
            {
                return;
            }

            SocketStream clientStream = ClientSocket.CreateStream(true);

            // Send one byte.
            var sendBuffer = new List<ArraySegment<byte>> { new byte[1] };
            await clientStream.InternalSendAsync(sendBuffer, false, default);

            // Accept the new stream on the server socket
            SocketStream serverStream = await ServerSocket.AcceptStreamAsync(default);

            // Continue reading from on the server socket and receive the byte sent over the client stream.
            _ = ServerSocket.AcceptStreamAsync(default).AsTask();
            int received = await serverStream.InternalReceiveAsync(new byte[256], default);
            Assert.That(received, Is.EqualTo(1));

            // Reset the stream
            clientStream.Reset(errorCode);

            // Ensure that receive on the server socket raises OperationCanceledException
            SocketStreamAbortedException? ex = Assert.CatchAsync<SocketStreamAbortedException>(
                async () => await serverStream.InternalReceiveAsync(new byte[1], default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));
            Assert.That(serverStream.CancelDispatchSource!.Token.IsCancellationRequested);
            clientStream.Release();
            serverStream.Release();

            // Ensure we can still send a request after the cancellation
            SocketStream clientStream2 = ClientSocket.CreateStream(true);
            await clientStream2.InternalSendAsync(sendBuffer, false, default);
            clientStream2.Release();
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
        public async Task MultiStreamSocketStream_ReceiveResponse_Cancellation1Async()
        {
            SocketStream stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveResponseFrameAsync(source.Token));
            stream.Release();
        }

        [Test]
        public async Task MultiStreamSocketStream_ReceiveResponse_Cancellation2Async()
        {
            SocketStream stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            SocketStream serverStream = await ServerSocket.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();
            _ = ServerSocket.AcceptStreamAsync(default).AsTask();

            using var source = new CancellationTokenSource();
            var responseTask = stream.ReceiveResponseFrameAsync(source.Token);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await responseTask);

            if (SocketType != MultiStreamSocketType.Ice1)
            {
                stream.Reset(SocketStreamErrorCode.InvocationCanceled);

                // Ensure the stream cancel dispatch source is canceled
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await Task.Delay(-1, serverStream.CancelDispatchSource!.Token));
            }

            stream.Release();
            serverStream.Release();
        }
    }
}
