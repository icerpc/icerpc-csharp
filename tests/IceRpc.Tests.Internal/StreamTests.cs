// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [Timeout(10000)]
    [TestFixture(MultiStreamConnectionType.Slic)]
    [TestFixture(MultiStreamConnectionType.Coloc)]
    [TestFixture(MultiStreamConnectionType.Ice1)]
    [Log(LogAttributeLevel.Debug)]
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
                (IceEncoder iceEncoder, ReadOnlyMemory<byte> value) => iceEncoder.WriteSequence(value.Span));

            var request = new OutgoingRequest(Proxy, "op", requestPayload, null, DateTime.MaxValue);
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

        [TestCase(RpcStreamError.DispatchCanceled)]
        [TestCase(RpcStreamError.InvocationCanceled)]
        [TestCase((RpcStreamError)10)]
        public async Task Stream_Abort(RpcStreamError errorCode)
        {
            // SendAsync/ReceiveAsync is only supported with Slic
            if (ConnectionType != MultiStreamConnectionType.Slic)
            {
                return;
            }

            RpcStream clientStream = ClientConnection.CreateStream(true);
            // Send one byte.
            await clientStream.SendAsync(CreateSendBuffer(clientStream, 1), false, default);

            // Accept the new stream on the server connection
            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);

            // Continue reading from on the server connection and receive the byte sent over the client stream.
            _ = ServerConnection.AcceptStreamAsync(default).AsTask();

            int received = await serverStream.ReceiveAsync(new byte[256], default);
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
                async () => await serverStream.SendResponseFrameAsync(GetResponseFrame(request), source.Token));
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
                async () => await stream.ReceiveResponseFrameAsync(source.Token));
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
            ValueTask<IncomingResponse> responseTask = stream.ReceiveResponseFrameAsync(source.Token);
            source.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await responseTask);

            if (ConnectionType != MultiStreamConnectionType.Ice1)
            {
                stream.Abort(RpcStreamError.InvocationCanceled);

                // Ensure the stream cancel dispatch source is canceled
                await dispatchCanceled.Task;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Stream_StreamReaderWriterAsync(bool cancelClientSide)
        {
            if (ConnectionType != MultiStreamConnectionType.Slic)
            {
                // TODO: add support for coloc once coloc streaming is simplified
                return;
            }

            RpcStream stream = ClientConnection.CreateStream(true);
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            _ = stream.SendAsync(CreateSendBuffer(stream, 1), false, default).AsTask();

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            _ = ServerConnection.AcceptStreamAsync(default).AsTask();
            await serverStream.ReceiveAsync(new byte[1], default);

            var sendStream = new TestMemoryStream(new byte[100]);
            new RpcStreamWriter(sendStream).Send(stream);

            byte[] readBuffer = new byte[100];
            Stream receiveStream = new RpcStreamReader(serverStream).ToByteStream();

            ValueTask<int> readTask = receiveStream.ReadAsync(readBuffer);
            await Task.Delay(100);
            Assert.That(readTask.IsCompleted, Is.False);
            sendStream.Semaphore.Release();
            Assert.That(await readTask, Is.EqualTo(100));

            ValueTask<int> readTask2 = receiveStream.ReadAsync(readBuffer);
            await Task.Delay(100);
            Assert.That(readTask2.IsCompleted, Is.False);

            if (cancelClientSide)
            {
                sendStream.Exception = new ArgumentException();
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

            public TestMemoryStream(byte[] buffer)
                : base(buffer)
            {
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
            {
                await Semaphore.WaitAsync(cancel);
                if (Exception != null)
                {
                    throw Exception;
                }
                Seek(0, SeekOrigin.Begin);
                return await base.ReadAsync(buffer, cancel);
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
