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
            ServerConnectionOptions.BidirectionalStreamMaxCount = 15;
            ServerConnectionOptions.UnidirectionalStreamMaxCount = 10;
            ServerConnectionOptions.IncomingFrameMaxSize = 512 * 1024;
        }

        [Test]
        public void MultiStreamConnection_Dispose()
        {
            ValueTask<RpcStream> acceptStreamTask = ServerConnection.AcceptStreamAsync(default);
            ClientConnection.Dispose();
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamConnection_AbortStreams_EmptyStreams()
        {
            ClientConnection.AbortStreams(RpcStreamError.ConnectionAborted);
            ServerConnection.AbortStreams(RpcStreamError.ConnectionAborted);

            (long clientBidirectional, long clientUnidirectional) = ClientConnection.Shutdown();
            (long serverBidirectional, long serverUnidirectional) = ServerConnection.Shutdown();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(2, serverUnidirectional); // client control stream ID = 2
        }

        [Test]
        public async Task MultiStreamConnection_AbortStreams_AbortStreamAsync()
        {
            RpcStream clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            ClientConnection.AbortStreams(RpcStreamError.ConnectionAborted);
            (long clientBidirectional, long clientUnidirectional) = ClientConnection.Shutdown();

            RpcStreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<RpcStreamAbortedException>(
                async () => await clientStream.ReceiveResponseFrameAsync(default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(RpcStreamError.ConnectionAborted));
            clientStream.Release();

            // Can't create new stream
            clientStream = ClientConnection.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.SendRequestFrameAsync(DummyRequest));

            (long serverBidirectional, long serverUnidirectional) = ServerConnection.Shutdown();

            clientStream.Release();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(2, serverUnidirectional); // client control stream ID = 2

            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_AbortStreams_NoAbortStreamAsync()
        {
            RpcStream clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            IncomingRequest incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(GetResponseFrame(incomingRequest));

            ClientConnection.AbortOutgoingStreams(RpcStreamError.ConnectionShutdown, (clientStream.Id, 0));

            // Stream is not aborted
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveResponseFrameAsync(default);

            (long serverBidirectional, long _) = ServerConnection.Shutdown();

            clientStream.Release();
            serverStream.Release();

            Assert.AreEqual(0, serverBidirectional);

            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_AbortStreams_LargestStreamIdsAsync()
        {
            var ex = new InvalidOperationException();

            RpcStream clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            IncomingRequest incomingRequest =
                await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(
                new OutgoingResponse(incomingRequest, new UnhandledException(ex)),
                default);

            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveResponseFrameAsync(default);

            clientStream.Release();
            serverStream.Release();

            clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            serverStream = await ServerConnection.AcceptStreamAsync(default);
            await serverStream.ReceiveRequestFrameAsync();

            (long clientBidirectional, long _) = ClientConnection.Shutdown();
            (long serverBidirectional, long _) = ServerConnection.Shutdown();

            clientStream.Release();
            serverStream.Release();

            // Check that largest stream IDs are correct
            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(4, serverBidirectional);

            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_AcceptStreamAsync()
        {
            RpcStream clientStream = ClientConnection.CreateStream(bidirectional: true);
            ValueTask<RpcStream> acceptTask = ServerConnection.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.SendRequestFrameAsync(DummyRequest);

            RpcStream serverStream = await acceptTask;

            Assert.That(serverStream.IsBidirectional, Is.True);
            Assert.That(serverStream.IsStarted, Is.True);
            Assert.That(serverStream.IsControl, Is.False);
            Assert.AreEqual(serverStream.Id, clientStream.Id);

            clientStream.Release();
            serverStream.Release();
        }

        [Test]
        public void MultiStreamConnection_AcceptStream_Cancellation()
        {
            using var source = new CancellationTokenSource();
            ValueTask<RpcStream> acceptTask = ServerConnection.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiStreamConnection_AcceptStream_Failure()
        {
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ServerConnection.AcceptStreamAsync(default));
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
                await ClientConnection.CloseAsync(0, source.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamConnection_CreateStream(bool bidirectional)
        {
            RpcStream clientStream = ClientConnection.CreateStream(bidirectional);
            Assert.That(clientStream.IsStarted, Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);
            Assert.That(clientStream.IsControl, Is.False);

            await clientStream.SendRequestFrameAsync(DummyRequest);
            Assert.That(clientStream.IsStarted, Is.True);
            Assert.That(clientStream.Id, Is.GreaterThanOrEqualTo(0));

            clientStream.Release();
        }

        [Test]
        public async Task MultiStreamConnection_StreamMaxCount_BidirectionalAsync()
        {
            var clientStreams = new List<RpcStream>();
            var serverStreams = new List<RpcStream>();
            IncomingRequest? incomingRequest = null;
            for (int i = 0; i < ServerConnectionOptions.BidirectionalStreamMaxCount; ++i)
            {
                RpcStream stream = ClientConnection.CreateStream(true);
                clientStreams.Add(stream);

                await stream.SendRequestFrameAsync(DummyRequest);

                serverStreams.Add(await ServerConnection.AcceptStreamAsync(default));
                IncomingRequest request = await serverStreams.Last().ReceiveRequestFrameAsync();
                incomingRequest ??= request;
            }

            // Ensure the client side accepts streams to receive responses.
            ValueTask<RpcStream> acceptClientStream = ClientConnection.AcceptStreamAsync(default);

            RpcStream clientStream = ClientConnection.CreateStream(true);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<RpcStream> acceptTask = ServerConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. For Ice1, the sending of the
            // request should succeed since the max count is only checked on the server side. For collocated and slic,
            // the stream isn't opened on the client side until we have confirmation from the server that we can open
            // a new stream, so the send shouldn't not complete.
            Assert.AreEqual(ConnectionType == MultiStreamConnectionType.Ice1, sendTask.IsCompleted);
            Assert.That(acceptTask.IsCompleted, Is.False);

            // Close one stream by sending the response (which sends the stream EOS) after receiving it.
            await serverStreams.Last().SendResponseFrameAsync(GetResponseFrame(incomingRequest!));
            await clientStreams.Last().ReceiveResponseFrameAsync();
            Assert.That(acceptClientStream.IsCompleted, Is.False);
            clientStreams.Last().Release();
            serverStreams.Last().Release();

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            RpcStream serverStream = await acceptTask;

            clientStream.Release();
            serverStream.Release();

            foreach (RpcStream stream in clientStreams)
            {
                stream.Release();
            }
            foreach (RpcStream stream in serverStreams)
            {
                stream.Release();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamConnection_StreamMaxCount_StressTestAsync(bool bidirectional)
        {
            int maxCount = bidirectional ?
                ServerConnectionOptions.BidirectionalStreamMaxCount :
                ServerConnectionOptions.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Ensure the client side accepts streams to receive responses.
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();

            // Send many requests and receive the responses.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendRequestAndReceiveResponseAsync(ClientConnection.CreateStream(bidirectional));
            }

            // Receive all the requests and send the responses.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveRequestAndSendResponseAsync(await ServerConnection.AcceptStreamAsync(default));
            }

            async Task SendRequestAndReceiveResponseAsync(RpcStream stream)
            {
                await stream.SendRequestFrameAsync(DummyRequest);

                // With non-Ice1 connections, the client-side keeps track of the stream max count and it ensures that it
                // doesn't open more streams that the server permits.
                if (ConnectionType != MultiStreamConnectionType.Ice1)
                {
                    Interlocked.Increment(ref streamCount);
                    Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));
                }
                if (bidirectional)
                {
                    await stream.ReceiveResponseFrameAsync();
                }
                stream.Release();
            }

            async Task ReceiveRequestAndSendResponseAsync(RpcStream stream)
            {
                // Ice1 stream max count is enforced on the server-side only. The stream is accepted only
                // the server-side stream count permits it.
                if (ConnectionType == MultiStreamConnectionType.Ice1)
                {
                    Interlocked.Increment(ref streamCount);
                    Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));
                }

                IncomingRequest request = await stream.ReceiveRequestFrameAsync();

                // With non-Ice1 connections, the server-side releases the stream shortly before sending the
                // last stream frame (with the response).
                if (ConnectionType != MultiStreamConnectionType.Ice1)
                {
                    Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));
                    Interlocked.Decrement(ref streamCount);
                }

                if (bidirectional)
                {
                    await stream.SendResponseFrameAsync(GetResponseFrame(request));
                }

                if (ConnectionType == MultiStreamConnectionType.Ice1)
                {
                    Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));
                    Interlocked.Decrement(ref streamCount);
                }
                stream.Release();
            }
        }

        [Test]
        public async Task MultiStreamConnection_StreamMaxCount_UnidirectionalAsync()
        {
            var clientStreams = new List<RpcStream>();
            var serverStreams = new List<RpcStream>();
            for (int i = 0; i < ServerConnectionOptions.UnidirectionalStreamMaxCount; ++i)
            {
                RpcStream stream = ClientConnection.CreateStream(false);
                clientStreams.Add(stream);
                await stream.SendRequestFrameAsync(DummyRequest);
                stream.Release();

                serverStreams.Add(await ServerConnection.AcceptStreamAsync(default));
                await serverStreams.Last().ReceiveRequestFrameAsync();
            }

            // Ensure the client side accepts streams to receive responses.
            ValueTask<RpcStream> acceptClientStream = ClientConnection.AcceptStreamAsync(default);

            RpcStream clientStream = ClientConnection.CreateStream(false);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<RpcStream> acceptTask = ServerConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. For Ice1, the sending of the
            // request should succeed since the max count is only checked on the server side. For collocated and slic,
            // the stream isn't opened on the client side until we have confirmation from the server that we can open
            // a new stream, so the send shouldn't not complete.
            Assert.AreEqual(ConnectionType == MultiStreamConnectionType.Ice1, sendTask.IsCompleted);
            Assert.That(acceptTask.IsCompleted, Is.False);

            // Close one stream by releasing the stream on the server-side.
            serverStreams.Last().Release();
            Assert.That(acceptClientStream.IsCompleted, Is.False);

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            RpcStream serverStream = await acceptTask;

            clientStream.Release();
            serverStream.Release();

            foreach (RpcStream stream in clientStreams)
            {
                stream.Release();
            }
            foreach (RpcStream stream in serverStreams)
            {
                stream.Release();
            }
        }

        [Test]
        public void MultiStreamConnection_PeerIncomingFrameMaxSize()
        {
            // PeerIncomingFrameMaxSize is set when control streams are initialized in Setup()
            if (ConnectionType == MultiStreamConnectionType.Ice1)
            {
                Assert.That(ServerConnection.PeerIncomingFrameMaxSize, Is.Null);
                Assert.That(ClientConnection.PeerIncomingFrameMaxSize, Is.Null);
            }
            else
            {
                Assert.AreEqual(ClientConnection.IncomingFrameMaxSize, ServerConnection.PeerIncomingFrameMaxSize!.Value);
                Assert.AreEqual(ServerConnection.IncomingFrameMaxSize, ClientConnection.PeerIncomingFrameMaxSize!.Value);
            }
        }

        [Test]
        public async Task MultiStreamConnection_PingAsync()
        {
            var semaphore = new SemaphoreSlim(0);
            ServerConnection.PingReceived = () => semaphore.Release();
            using var source = new CancellationTokenSource();

            // Start accept stream on the server side to receive transport frames.
            ValueTask<RpcStream> acceptStreamTask = ServerConnection.AcceptStreamAsync(source.Token);

            await ClientConnection.PingAsync(default);
            await semaphore.WaitAsync();

            await ClientConnection.PingAsync(default);
            await ClientConnection.PingAsync(default);
            await ClientConnection.PingAsync(default);
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
            Assert.ThrowsAsync<OperationCanceledException>(async () => await ClientConnection.PingAsync(source.Token));
        }

        [Test]
        public void MultiStreamConnection_Ping_Failure()
        {
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientConnection.PingAsync(default));
        }

        [Test]
        public void MultiStreamConnection_Properties()
        {
            Test(ClientConnection);
            Test(ServerConnection);

            Assert.That(ClientConnection.IsServer, Is.False);
            Assert.That(ServerConnection.IsServer, Is.True);

            static void Test(MultiStreamConnection connection)
            {
                Assert.That(connection.LocalEndpoint, Is.Not.Null);
                Assert.AreNotEqual(connection.IdleTimeout, TimeSpan.Zero);
                Assert.That(connection.IncomingFrameMaxSize, Is.GreaterThan(0));
                if (connection.Protocol != Protocol.Ice1)
                {
                    Assert.That(connection.PeerIncomingFrameMaxSize, Is.GreaterThan(0));
                }
                else
                {
                    Assert.That(connection.PeerIncomingFrameMaxSize, Is.Null);
                }
                Assert.That(connection.ToString(), Is.Not.Empty);
                Assert.AreNotEqual(connection.LastActivity, TimeSpan.Zero);
                Assert.AreEqual(0, connection.LastResponseStreamId);
                Assert.AreEqual(0, connection.IncomingStreamCount);
                Assert.AreEqual(0, connection.OutgoingStreamCount);
            }

            Assert.AreEqual(512 * 1024, ServerConnection.IncomingFrameMaxSize);
            Assert.AreEqual(1024 * 1024, ClientConnection.IncomingFrameMaxSize);
        }

        [Test]
        public void MultiStreamConnection_SendRequest_Failure()
        {
            RpcStream stream = ClientConnection.CreateStream(false);
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await stream.SendRequestFrameAsync(DummyRequest));
        }

        [Test]
        public async Task MultiStreamConnection_SendResponse_FailureAsync()
        {
            RpcStream stream = ClientConnection.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            RpcStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            IncomingRequest request = await serverStream.ReceiveRequestFrameAsync();
            ServerConnection.Dispose();
            Assert.CatchAsync<RpcStreamAbortedException>(
                async () => await serverStream.SendResponseFrameAsync(GetResponseFrame(request)));
        }

        [Order(1)]
        public async Task MultiStreamConnection_StreamCountAsync()
        {
            Assert.AreEqual(0, ClientConnection.IncomingStreamCount);
            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
            Assert.AreEqual(0, ServerConnection.OutgoingStreamCount);

            Action release1 = await TestAsync(ClientConnection, ServerConnection, 1);
            Action release2 = await TestAsync(ServerConnection, ClientConnection, 1);

            Action release3 = await TestAsync(ClientConnection, ServerConnection, 2);
            Action release4 = await TestAsync(ServerConnection, ClientConnection, 2);

            release4();
            release3();

            release2();
            release1();

            async Task<Action> TestAsync(MultiStreamConnection connection, MultiStreamConnection peerConnection, int expectedCount)
            {
                RpcStream clientStream = connection.CreateStream(true);
                Assert.AreEqual(expectedCount - 1, connection.OutgoingStreamCount);
                ValueTask task = clientStream.SendRequestFrameAsync(DummyRequest);
                Assert.AreEqual(expectedCount, connection.OutgoingStreamCount);

                Assert.AreEqual(expectedCount - 1, peerConnection.IncomingStreamCount);
                RpcStream serverStream = await peerConnection.AcceptStreamAsync(default);
                Assert.AreEqual(expectedCount, peerConnection.IncomingStreamCount);

                await task;
                return () =>
                {
                    clientStream.Release();
                    Assert.AreEqual(expectedCount - 1, connection.OutgoingStreamCount);

                    serverStream.Release();
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
