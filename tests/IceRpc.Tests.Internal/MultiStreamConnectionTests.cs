// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Test the varions multi-stream connection implementations.
    [Timeout(30000)]
    [TestFixture(MultiStreamConnectionType.Slic)]
    [TestFixture(MultiStreamConnectionType.Coloc)]
    [TestFixture(MultiStreamConnectionType.Ice1)]
    public class MultiStreamConnectionTests : MultiStreamConnectionBaseTest
    {
        public MultiStreamConnectionTests(MultiStreamConnectionType type)
            : base(type)
        {
            IncomingConnectionOptions.BidirectionalStreamMaxCount = 15;
            IncomingConnectionOptions.UnidirectionalStreamMaxCount = 10;
            IncomingConnectionOptions.IncomingFrameMaxSize = 512 * 1024;
        }

        [Test]
        public void MultiStreamConnection_Dispose()
        {
            ValueTask<Stream> acceptStreamTask = IncomingConnection.AcceptStreamAsync(default);
            OutgoingConnection.Dispose();
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamConnection_Dispose_EmptyStreams()
        {
            OutgoingConnection.Dispose();
            IncomingConnection.Dispose();

            (long clientBidirectional, long clientUnidirectional) = OutgoingConnection.Shutdown();
            (long serverBidirectional, long serverUnidirectional) = IncomingConnection.Shutdown();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(2, serverUnidirectional); // client control stream ID = 2
        }

        [Test]
        public async Task MultiStreamConnection_Dispose_StreamAbortedAsync()
        {
            var clientStream = OutgoingConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            OutgoingConnection.Dispose();
            (long clientBidirectional, long clientUnidirectional) = OutgoingConnection.Shutdown();

            StreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<StreamAbortedException>(
                async () => await clientStream.ReceiveResponseFrameAsync(default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(StreamErrorCode.ConnectionAborted));

            // Can't create new stream
            clientStream = OutgoingConnection.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.SendRequestFrameAsync(DummyRequest));

            (long serverBidirectional, long serverUnidirectional) = IncomingConnection.Shutdown();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(2, serverUnidirectional); // client control stream ID = 2

            Assert.AreEqual(0, OutgoingConnection.OutgoingStreamCount);
            Assert.AreEqual(0, IncomingConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_AbortOutgoingStreams_NoAbortStreamAsync()
        {
            var ex = new InvalidOperationException();

            var clientStream = OutgoingConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await IncomingConnection.AcceptStreamAsync(default);
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(GetResponseFrame(incomingRequest));

            OutgoingConnection.AbortOutgoingStreams(StreamErrorCode.ConnectionShutdown, (clientStream.Id, 0));

            // Stream is not aborted
            _ = OutgoingConnection.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveResponseFrameAsync(default);

            (long serverBidirectional, long _) = IncomingConnection.Shutdown();

            Assert.AreEqual(0, serverBidirectional);

            Assert.AreEqual(0, OutgoingConnection.OutgoingStreamCount);
            Assert.AreEqual(0, IncomingConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_LargestStreamIdsAsync()
        {
            var ex = new InvalidOperationException();

            var clientStream = OutgoingConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await IncomingConnection.AcceptStreamAsync(default);
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(
                new OutgoingResponse(incomingRequest, new UnhandledException(ex)),
                default);

            _ = OutgoingConnection.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveResponseFrameAsync(default);

            clientStream = OutgoingConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            serverStream = await IncomingConnection.AcceptStreamAsync(default);
            await serverStream.ReceiveRequestFrameAsync();

            (long clientBidirectional, long _) = OutgoingConnection.Shutdown();
            (long serverBidirectional, long _) = IncomingConnection.Shutdown();

            // Check that largest stream IDs are correct
            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(4, serverBidirectional);

            // Terminate the streams
            await serverStream.SendResponseFrameAsync(
                new OutgoingResponse(incomingRequest, new UnhandledException(ex)),
                default);
            await clientStream.ReceiveResponseFrameAsync(default);

            Assert.AreEqual(0, OutgoingConnection.OutgoingStreamCount);
            Assert.AreEqual(0, IncomingConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_AcceptStreamAsync()
        {
            Stream clientStream = OutgoingConnection.CreateStream(bidirectional: true);
            ValueTask<Stream> acceptTask = IncomingConnection.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.SendRequestFrameAsync(DummyRequest);

            Stream serverStream = await acceptTask;

            Assert.IsTrue(serverStream.IsBidirectional);
            Assert.IsTrue(serverStream.IsStarted);
            Assert.IsFalse(serverStream.IsControl);
            Assert.AreEqual(serverStream.Id, clientStream.Id);
        }

        [Test]
        public void MultiStreamConnection_AcceptStream_Cancellation()
        {
            using var source = new CancellationTokenSource();
            ValueTask<Stream> acceptTask = IncomingConnection.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiStreamConnection_AcceptStream_Failure()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await IncomingConnection.AcceptStreamAsync(default));
        }

        [Test]
        public async Task MultiStreamConnection_CloseAsync_CancellationAsync()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException depending on the
                // implementation (which might be a no-op).
                await OutgoingConnection.CloseAsync(0, source.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamConnection_CreateStream(bool bidirectional)
        {
            Stream clientStream = OutgoingConnection.CreateStream(bidirectional);
            Assert.IsFalse(clientStream.IsStarted);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);
            Assert.IsFalse(clientStream.IsControl);

            await clientStream.SendRequestFrameAsync(DummyRequest);
            Assert.IsTrue(clientStream.IsStarted);
            Assert.GreaterOrEqual(clientStream.Id, 0);
        }

        [Test]
        public async Task MultiStreamConnection_StreamMaxCount_BidirectionalAsync()
        {
            var clientStreams = new List<Stream>();
            var serverStreams = new List<Stream>();
            IncomingRequest? incomingRequest = null;
            for (int i = 0; i < IncomingConnectionOptions.BidirectionalStreamMaxCount; ++i)
            {
                var stream = OutgoingConnection.CreateStream(true);
                clientStreams.Add(stream);

                await stream.SendRequestFrameAsync(DummyRequest);

                serverStreams.Add(await IncomingConnection.AcceptStreamAsync(default));
                var request = await serverStreams.Last().ReceiveRequestFrameAsync();
                incomingRequest ??= request;
            }

            // Ensure the client side accepts streams to receive responses.
            ValueTask<Stream> acceptClientStream = OutgoingConnection.AcceptStreamAsync(default);

            var clientStream = OutgoingConnection.CreateStream(true);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<Stream> acceptTask = IncomingConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. For Ice1, the sending of the
            // request should succeed since the max count is only checked on the server side. For collocated and slic,
            // the stream isn't opened on the client side until we have confirmation from the server that we can open
            // a new stream, so the send shouldn't not complete.
            Assert.AreEqual(ConnectionType == MultiStreamConnectionType.Ice1, sendTask.IsCompleted);
            Assert.IsFalse(acceptTask.IsCompleted);

            // Close one stream by sending the response (which sends the stream EOS) after receiving it.
            await serverStreams.Last().SendResponseFrameAsync(GetResponseFrame(incomingRequest!));
            await clientStreams.Last().ReceiveResponseFrameAsync();
            Assert.IsFalse(acceptClientStream.IsCompleted);

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            _ = await acceptTask;
        }

        [TestCase(false)]
        // [TestCase(true)]
        public async Task MultiStreamConnection_StreamMaxCount_StressTestAsync(bool bidirectional)
        {
            int maxCount = bidirectional ?
                IncomingConnectionOptions.BidirectionalStreamMaxCount :
                IncomingConnectionOptions.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Ensure the client side accepts streams to receive responses.
            _ = OutgoingConnection.AcceptStreamAsync(default).AsTask();

            // Send many requests and receive the responses.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendRequestAndReceiveResponseAsync(OutgoingConnection.CreateStream(bidirectional));
            }

            // Receive all the requests and send the responses.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveRequestAndSendResponseAsync(await IncomingConnection.AcceptStreamAsync(default));
            }

            async Task SendRequestAndReceiveResponseAsync(Stream stream)
            {
                await stream.SendRequestFrameAsync(DummyRequest);

                if (ConnectionType != MultiStreamConnectionType.Ice1)
                {
                    // With non-Ice1 connections, the client-side keeps track of the stream max count and it
                    // ensures that it doesn't open more streams that the server permits.
                    Interlocked.Increment(ref streamCount);
                }

                Assert.LessOrEqual(Thread.VolatileRead(ref streamCount), maxCount);

                if (bidirectional)
                {
                    await stream.ReceiveResponseFrameAsync();
                }
            }

            async Task ReceiveRequestAndSendResponseAsync(Stream stream)
            {
                if (ConnectionType == MultiStreamConnectionType.Ice1)
                {
                    // Ice1 stream max count is enforced on the server-side only. The stream is accepted only
                    // the server-side stream count permits it.
                    Interlocked.Increment(ref streamCount);
                }

                // Make sure the connection didn't accept more streams than it is allowed to.
                Assert.LessOrEqual(Thread.VolatileRead(ref streamCount), maxCount);

                if (!bidirectional)
                {
                    // The stream is terminated as soon as the last frame of the request is received, so we have
                    // to decrement the count here before the request receive completes.
                    Interlocked.Decrement(ref streamCount);
                }

                var request = await stream.ReceiveRequestFrameAsync();

                if (bidirectional)
                {
                    Interlocked.Decrement(ref streamCount);
                }

                if (bidirectional)
                {
                    await stream.SendResponseFrameAsync(GetResponseFrame(request));
                }
            }
        }

        [Test]
        public async Task MultiStreamConnection_StreamMaxCount_UnidirectionalAsync()
        {
            var clientStreams = new List<Stream>();
            for (int i = 0; i < IncomingConnectionOptions.UnidirectionalStreamMaxCount; ++i)
            {
                var stream = OutgoingConnection.CreateStream(false);
                clientStreams.Add(stream);
                await stream.SendRequestFrameAsync(DummyRequest);
            }

            // Ensure the client side accepts streams to receive acknowledgement of stream completion.
            ValueTask<Stream> acceptClientStream = OutgoingConnection.AcceptStreamAsync(default);

            var clientStream = OutgoingConnection.CreateStream(false);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);

            // Accept a new unidirectional stream. This shouldn't allow the new stream send request to
            // complete since the request wasn't read yet on the stream.
            var serverStream = await IncomingConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. For Ice1, the sending of the
            // request should succeed since the max count is only checked on the server side. For collocated and slic,
            // the stream isn't opened on the client side until we have confirmation from the server that we can open
            // a new stream, so the send shouldn't not complete.
            Assert.AreEqual(ConnectionType == MultiStreamConnectionType.Ice1, sendTask.IsCompleted);

            // Close the server-side stream by receiving the request from the stream.
            await serverStream.ReceiveRequestFrameAsync();

            Assert.IsFalse(acceptClientStream.IsCompleted);

            // The send task of the new stream should now succeed.
            await sendTask;
        }

        [Test]
        public void MultiStreamConnection_PeerIncomingFrameMaxSize()
        {
            // PeerIncomingFrameMaxSize is set when control streams are initialized in Setup()
            if (ConnectionType == MultiStreamConnectionType.Ice1)
            {
                Assert.IsNull(IncomingConnection.PeerIncomingFrameMaxSize);
                Assert.IsNull(OutgoingConnection.PeerIncomingFrameMaxSize);
            }
            else
            {
                Assert.AreEqual(OutgoingConnection.IncomingFrameMaxSize, IncomingConnection.PeerIncomingFrameMaxSize!.Value);
                Assert.AreEqual(IncomingConnection.IncomingFrameMaxSize, OutgoingConnection.PeerIncomingFrameMaxSize!.Value);
            }
        }

        [Test]
        public async Task MultiStreamConnection_PingAsync()
        {
            var semaphore = new SemaphoreSlim(0);
            IncomingConnection.PingReceived = () => semaphore.Release();
            using var source = new CancellationTokenSource();

            // Start accept stream on the server side to receive transport frames.
            var acceptStreamTask = IncomingConnection.AcceptStreamAsync(source.Token);

            await OutgoingConnection.PingAsync(default);
            await semaphore.WaitAsync();

            await OutgoingConnection.PingAsync(default);
            await OutgoingConnection.PingAsync(default);
            await OutgoingConnection.PingAsync(default);
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            // Cancel AcceptStreamAsync
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamConnection_Ping_Cancellation()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await OutgoingConnection.PingAsync(source.Token));
        }

        [Test]
        public void MultiStreamConnection_Ping_Failure()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await OutgoingConnection.PingAsync(default));
        }

        [Test]
        public void MultiStreamConnection_Properties()
        {
            Test(OutgoingConnection);
            Test(IncomingConnection);

            Assert.IsTrue(!OutgoingConnection.IsIncoming);
            Assert.IsTrue(IncomingConnection.IsIncoming);

            static void Test(MultiStreamConnection connection)
            {
                Assert.NotNull(connection.LocalEndpoint != null);
                Assert.AreNotEqual(connection.IdleTimeout, TimeSpan.Zero);
                Assert.Greater(connection.IncomingFrameMaxSize, 0);
                if (connection.Protocol != Protocol.Ice1)
                {
                    Assert.Greater(connection.PeerIncomingFrameMaxSize, 0);
                }
                else
                {
                    Assert.IsNull(connection.PeerIncomingFrameMaxSize);
                }
                Assert.IsNotEmpty(connection.ToString());
                Assert.AreNotEqual(connection.LastActivity, TimeSpan.Zero);
                Assert.AreEqual(0, connection.LastResponseStreamId);
                Assert.AreEqual(0, connection.IncomingStreamCount);
                Assert.AreEqual(0, connection.OutgoingStreamCount);
            }

            Assert.AreEqual(512 * 1024, IncomingConnection.IncomingFrameMaxSize);
            Assert.AreEqual(1024 * 1024, OutgoingConnection.IncomingFrameMaxSize);
        }

        [Test]
        public void MultiStreamConnection_SendRequest_Failure()
        {
            var stream = OutgoingConnection.CreateStream(false);
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await stream.SendRequestFrameAsync(DummyRequest));
        }

        [Test]
        public async Task MultiStreamConnection_SendResponse_FailureAsync()
        {
            var stream = OutgoingConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await IncomingConnection.AcceptStreamAsync(default);
            var request = await serverStream.ReceiveRequestFrameAsync();
            IncomingConnection.Dispose();
            Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.SendResponseFrameAsync(GetResponseFrame(request)));
        }

        [Order(1)]
        public async Task MultiStreamConnection_StreamCountAsync()
        {
            Assert.AreEqual(0, OutgoingConnection.IncomingStreamCount);
            Assert.AreEqual(0, OutgoingConnection.OutgoingStreamCount);
            Assert.AreEqual(0, IncomingConnection.IncomingStreamCount);
            Assert.AreEqual(0, IncomingConnection.OutgoingStreamCount);

            var release1 = await TestAsync(OutgoingConnection, IncomingConnection, 1);
            var release2 = await TestAsync(IncomingConnection, OutgoingConnection, 1);

            var release3 = await TestAsync(OutgoingConnection, IncomingConnection, 2);
            var release4 = await TestAsync(IncomingConnection, OutgoingConnection, 2);

            release4();
            release3();

            release2();
            release1();

            async Task<Action> TestAsync(MultiStreamConnection connection, MultiStreamConnection peerConnection, int expectedCount)
            {
                var clientStream = connection.CreateStream(true);
                Assert.AreEqual(expectedCount - 1, connection.OutgoingStreamCount);
                ValueTask task = clientStream.SendRequestFrameAsync(DummyRequest);
                Assert.AreEqual(expectedCount, connection.OutgoingStreamCount);

                Assert.AreEqual(expectedCount - 1, peerConnection.IncomingStreamCount);
                var serverStream = await peerConnection.AcceptStreamAsync(default);
                Assert.AreEqual(expectedCount, peerConnection.IncomingStreamCount);

                await task;
                return () =>
                {
                    Assert.AreEqual(expectedCount - 1, connection.OutgoingStreamCount);
                    Assert.AreEqual(expectedCount - 1, peerConnection.IncomingStreamCount);
                };
            }
        }

        [SetUp]
        public Task SetUp() => SetUpConnectionsAsync();

        [TearDown]
        public void TearDown() => TearDownConnections();
    }
}
