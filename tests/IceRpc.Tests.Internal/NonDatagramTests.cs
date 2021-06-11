// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [TestFixture("tcp", false, AddressFamily.InterNetwork)]
    [TestFixture("ws", false, AddressFamily.InterNetwork)]
    [TestFixture("tcp", true, AddressFamily.InterNetwork)]
    [TestFixture("ws", true, AddressFamily.InterNetwork)]
    [TestFixture("tcp", false, AddressFamily.InterNetworkV6)]
    [Timeout(30000)]
    public class NonDatagramTests : SingleStreamConnectionBaseTest
    {
        public NonDatagramTests(string transport, bool tls, AddressFamily addressFamily)
            : base(Protocol.Ice2, transport, tls, addressFamily)
        {
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> receiveTask = OutgoingConnection.ReceiveAsync(new byte[1], canceled.Token);
            Assert.IsFalse(receiveTask.IsCompleted);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_ConnectionLostException()
        {
            IncomingConnection.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () => await OutgoingConnection.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_Dispose()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await OutgoingConnection.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramConnection_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await OutgoingConnection.ReceiveAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await OutgoingConnection.ReceiveAsync(new byte[1], canceled.Token));
        }

        [Test]
        public void NonDatagramConnection_ReceiveDatagramAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await OutgoingConnection.ReceiveDatagramAsync(default));
        }

        [Test]
        public async Task NonDatagramConnection_SendAsync_CancellationAsync()
        {
            IncomingConnection.NetworkSocket!.ReceiveBufferSize = 4096;
            OutgoingConnection.NetworkSocket!.SendBufferSize = 4096;

            // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
            // are at least not larger than 16KB. The test below relies on the SendAsync to block when the connection
            // send/receive buffers fill up.
            Assert.Less(IncomingConnection.NetworkSocket!.ReceiveBufferSize, 16 * 1024);
            Assert.Less(OutgoingConnection.NetworkSocket!.SendBufferSize, 16 * 1024);

            using var canceled = new CancellationTokenSource();

            // Wait for the SendAsync call to block.
            Task<int> sendTask;
            do
            {
                sendTask = OutgoingConnection.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
                await Task.WhenAny(Task.Delay(500), sendTask);
            }
            while (sendTask.IsCompleted);
            sendTask = OutgoingConnection.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
            Assert.IsFalse(sendTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await sendTask);
        }

        [Test]
        public void NonDatagramConnection_SendAsync_ConnectionLostException()
        {
            IncomingConnection.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () =>
                {
                    while (true)
                    {
                        await OutgoingConnection.SendAsync(OneMBSendBuffer, default);
                    }
                });
        }

        [Test]
        public void NonDatagramConnection_SendAsync_Dispose()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await OutgoingConnection.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void NonDatagramConnection_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await OutgoingConnection.SendAsync(OneBSendBuffer, canceled.Token));
        }

        [Test]
        public void NonDatagramConnection_SendDatagramAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await OutgoingConnection.SendDatagramAsync(OneBSendBuffer, default));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(16 * 1024)]
        [TestCase(512 * 1024)]
        public async Task NonDatagramConnection_SendReceiveAsync(int size)
        {
            var sendBuffer = new byte[size];

            ValueTask test1 = Test(OutgoingConnection, IncomingConnection);
            ValueTask test2 = Test(IncomingConnection, OutgoingConnection);

            await test1;
            await test2;

            async ValueTask Test(SingleStreamConnection connection1, SingleStreamConnection connection2)
            {
                ValueTask<int> sendTask = connection1.SendAsync(sendBuffer, default);
                Memory<byte> receiveBuffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    offset += await connection2.ReceiveAsync(receiveBuffer[offset..], default);
                }
                Assert.AreEqual(await sendTask, size);
            }
        }
    }
}
