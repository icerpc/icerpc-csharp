// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

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

        [TestCase(64)]
        [TestCase(1024)]
        [TestCase(32 * 1024)]
        [TestCase(128 * 1024)]
        [TestCase(512 * 1024)]
        public async Task Stream_SendReceiveRequestAsync(int size)
        {
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload = Payload.FromSingleArg(
                Proxy,
                new byte[size],
                (OutputStream ostr, ReadOnlyMemory<byte> value) => ostr.WriteSequence(value.Span));

            var request = new OutgoingRequest(Proxy, "op", requestPayload, DateTime.MaxValue);
            ValueTask receiveTask = PerformReceiveAsync();

            RpcStream stream = ClientConnection.CreateStream(false);
            await stream.SendRequestFrameAsync(request);
            stream.Release();

            await receiveTask;

            async ValueTask PerformReceiveAsync()
            {
                RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
                ValueTask<RpcStream> _ = ServerConnection.AcceptStreamAsync(default);
                await serverStream.ReceiveRequestFrameAsync();
                serverStream.Release();
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
            stream.Release();
        }

        [TestCase(RpcStreamError.DispatchCanceled)]
        [TestCase(RpcStreamError.InvocationCanceled)]
        [TestCase((RpcStreamError)10)]
        public async Task Stream_Reset(RpcStreamError errorCode)
        {
            // SendAsync/ReceiveAsync is only supported with Slic
            if (ConnectionType != MultiStreamConnectionType.Slic)
            {
                return;
            }

            RpcStream clientStream = ClientConnection.CreateStream(true);

            // Send one byte.
            var sendBuffer = new ReadOnlyMemory<byte>[] { new byte[1] };
            await clientStream.InternalSendAsync(sendBuffer, false, default);

            // Accept the new stream on the server connection
            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);

            // Continue reading from on the server connection and receive the byte sent over the client stream.
            _ = ServerConnection.AcceptStreamAsync(default).AsTask();
            int received = await serverStream.InternalReceiveAsync(new byte[256], default);
            Assert.That(received, Is.EqualTo(1));

            // Reset the stream
            clientStream.Reset(errorCode);

            // Ensure that receive on the server connection raises OperationCanceledException
            RpcStreamAbortedException? ex = Assert.CatchAsync<RpcStreamAbortedException>(
                async () => await serverStream.InternalReceiveAsync(new byte[1], default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));
            Assert.That(serverStream.CancelDispatchSource!.Token.IsCancellationRequested);
            clientStream.Release();
            serverStream.Release();

            // Ensure we can still send a request after the cancellation
            RpcStream clientStream2 = ClientConnection.CreateStream(true);
            await clientStream2.InternalSendAsync(sendBuffer, false, default);
            clientStream2.Release();
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
                async () => await stream.SendResponseFrameAsync(GetResponseFrame(request), source.Token));

            stream.Release();
            serverStream.Release();
        }

        [Test]
        public void Stream_ReceiveRequest_Cancellation()
        {
            RpcStream stream = ClientConnection.CreateStream(false);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveRequestFrameAsync(source.Token));
            stream.Release();
        }

        [Test]
        public async Task Stream_ReceiveResponse_Cancellation1Async()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveResponseFrameAsync(source.Token));
            stream.Release();
        }

        [Test]
        public async Task Stream_ReceiveResponse_Cancellation2Async()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();
            _ = ServerConnection.AcceptStreamAsync(default).AsTask();

            using var source = new CancellationTokenSource();
            ValueTask<IncomingResponse> responseTask = stream.ReceiveResponseFrameAsync(source.Token);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await responseTask);

            if (ConnectionType != MultiStreamConnectionType.Ice1)
            {
                stream.Reset(RpcStreamError.InvocationCanceled);

                // Ensure the stream cancel dispatch source is canceled
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await Task.Delay(-1, serverStream.CancelDispatchSource!.Token));
            }

            stream.Release();
            serverStream.Release();
        }
    }
}
