// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [TestFixture("tcp", false, AddressFamily.InterNetwork)]
    [TestFixture("tcp", true, AddressFamily.InterNetwork)]
    [TestFixture("tcp", false, AddressFamily.InterNetworkV6)]
    [Timeout(30000)]
    public class NonDatagramTests : NetworkSocketConnectionBaseTest
    {
        public NonDatagramTests(string transport, bool tls, AddressFamily addressFamily)
            : base(Protocol.Ice2, transport, tls, addressFamily)
        {
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> receiveTask = ClientConnection.ReceiveAsync(new byte[1], canceled.Token);
            Assert.That(receiveTask.IsCompleted, Is.False);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_ConnectionLostException()
        {
            ServerConnection.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () => await ClientConnection.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_Dispose()
        {
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientConnection.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await ClientConnection.ReceiveAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientConnection.ReceiveAsync(new byte[1], canceled.Token));
        }

        // [Test]
        // TODO: why support cancellation of SendAsync on a TCP connection?
        public async Task NonDatagramConnection_SendAsync_CancellationAsync()
        {
            ServerConnection.Socket!.ReceiveBufferSize = 4096;
            ClientConnection.Socket!.SendBufferSize = 4096;

            // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
            // are at least not larger than 16KB. The test below relies on the SendAsync to block when the connection
            // send/receive buffers fill up.
            Assert.That(ServerConnection.Socket!.ReceiveBufferSize, Is.LessThan(16 * 1024));
            Assert.That(ClientConnection.Socket!.SendBufferSize, Is.LessThan(16 * 1024));

            using var canceled = new CancellationTokenSource();

            // Wait for the SendAsync call to block.
            Task sendTask;
            do
            {
                sendTask = ClientConnection.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
                await Task.WhenAny(Task.Delay(500), sendTask);
            }
            while (sendTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await sendTask);
        }

        [Test]
        public void NonDatagramConnection_SendAsync_ConnectionLostException()
        {
            ServerConnection.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () =>
                {
                    while (true)
                    {
                        await ClientConnection.SendAsync(OneMBSendBuffer, default);
                    }
                });
        }

        [Test]
        public void NonDatagramConnection_SendAsync_Dispose()
        {
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientConnection.SendAsync(OneBSendBuffer, default));
        }

        // [Test]
        // See TODO above
        public void NonDatagramConnection_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientConnection.SendAsync(OneBSendBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(16 * 1024)]
        [TestCase(512 * 1024)]
        public async Task NonDatagramConnection_SendReceiveAsync(int size)
        {
            byte[] sendBuffer = new byte[size];

            ValueTask test1 = Test(ClientConnection, ServerConnection);
            ValueTask test2 = Test(ServerConnection, ClientConnection);

            await test1;
            await test2;

            async ValueTask Test(NetworkSocket connection1, NetworkSocket connection2)
            {
                ValueTask sendTask = connection1.SendAsync(sendBuffer, default);
                Memory<byte> receiveBuffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    offset += await connection2.ReceiveAsync(receiveBuffer[offset..], default);
                }
            }
        }
    }
}
