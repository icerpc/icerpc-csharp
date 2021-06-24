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

            Stream stream = OutgoingConnection.CreateStream(false);
            await stream.SendRequestFrameAsync(request);

            await receiveTask;

            async ValueTask PerformReceiveAsync()
            {
                Stream serverStream = await IncomingConnection.AcceptStreamAsync(default);
                ValueTask<Stream> _ = IncomingConnection.AcceptStreamAsync(default);
                await serverStream.ReceiveRequestFrameAsync();
            }
        }

        [Test]
        public void Stream_SendRequestAsync_Cancellation()
        {
            Stream stream = OutgoingConnection.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendRequestFrameAsync(DummyRequest, source.Token));
        }

        [TestCase(StreamErrorCode.DispatchCanceled)]
        [TestCase(StreamErrorCode.InvocationCanceled)]
        [TestCase((StreamErrorCode)10)]
        public async Task Stream_Reset(StreamErrorCode errorCode)
        {
            // SendAsync/ReceiveAsync is only supported with Slic
            if (ConnectionType != MultiStreamConnectionType.Slic)
            {
                return;
            }

            Stream clientStream = OutgoingConnection.CreateStream(true);

            // Send one byte.
            var sendBuffer = new ReadOnlyMemory<byte>[] { new byte[1] };
            await clientStream.InternalSendAsync(sendBuffer, false, default);

            // Accept the new stream on the incoming connection
            Stream serverStream = await IncomingConnection.AcceptStreamAsync(default);

            // Continue reading from on the incoming connection and receive the byte sent over the client stream.
            _ = IncomingConnection.AcceptStreamAsync(default).AsTask();
            int received = await serverStream.InternalReceiveAsync(new byte[256], default);
            Assert.That(received, Is.EqualTo(1));

            // Reset the stream
            clientStream.Abort(errorCode);

            // Ensure that receive on the incoming connection raises OperationCanceledException
            StreamAbortedException? ex = Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.InternalReceiveAsync(new byte[1], default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(errorCode));
            Assert.That(serverStream.CancelDispatchSource!.Token.IsCancellationRequested);

            // Ensure we can still send a request after the cancellation
            Stream clientStream2 = OutgoingConnection.CreateStream(true);
            await clientStream2.InternalSendAsync(sendBuffer, true, default);
        }

        [Test]
        public async Task Stream_SendResponse_CancellationAsync()
        {
            Stream stream = OutgoingConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            Stream serverStream = await IncomingConnection.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();

            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendResponseFrameAsync(GetResponseFrame(request), source.Token));
        }

        [Test]
        public void Stream_ReceiveRequest_Cancellation()
        {
            Stream stream = OutgoingConnection.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveRequestFrameAsync(source.Token));
        }

        [Test]
        public async Task Stream_ReceiveResponse_Cancellation1Async()
        {
            Stream stream = OutgoingConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveResponseFrameAsync(source.Token));
        }

        [Test]
        public async Task Stream_ReceiveResponse_Cancellation2Async()
        {
            Stream stream = OutgoingConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            Stream serverStream = await IncomingConnection.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();
            _ = IncomingConnection.AcceptStreamAsync(default).AsTask();

            using var source = new CancellationTokenSource();
            ValueTask<IncomingResponse> responseTask = stream.ReceiveResponseFrameAsync(source.Token);
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await responseTask);

            if (ConnectionType != MultiStreamConnectionType.Ice1)
            {
                stream.Abort(StreamErrorCode.InvocationCanceled);

                // Ensure the stream cancel dispatch source is canceled
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await Task.Delay(-1, serverStream.CancelDispatchSource!.Token));
            }
        }
    }
}
