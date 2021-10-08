﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture(ProtocolCode.Ice1, "tcp")]
    [TestFixture(ProtocolCode.Ice2, "tcp")]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class StressTests : ClientServerBaseTest
    {
        private Connection Connection { get; }
        private Server Server { get; }
        private string Transport { get; }
        private IStressTestPrx Prx { get; }
        private StressTest Servant { get; }

        public StressTests(ProtocolCode protocol, string transport)
        {
            Transport = transport;
            Servant = new StressTest();
            Endpoint serverEndpoint = GetTestEndpoint(
                protocol: Protocol.FromProtocolCode(protocol),
                transport: Transport);
            Server = new Server
            {
                Dispatcher = Servant,
                Endpoint = serverEndpoint
            };
            Connection = new Connection
            {
                RemoteEndpoint = serverEndpoint
            };
            Prx = StressTestPrx.FromConnection(Connection);
            Server.Listen();
        }

        [TearDown]
        public async Task DisposeAsync()
        {
            await Server.DisposeAsync();
            await Connection.ShutdownAsync();
        }

        [Test]
        public async Task Stress_Send_ByteSeq([Range(0, 2048, 1024)] int size)
        {
            var data = Enumerable.Range(0, size).Select(x => (byte)x).ToArray();
            await Prx.OpSendByteSeqAsync(data);
            CollectionAssert.AreEqual(data, Servant.OpSendByteSeqData);
        }

        [Test]
        public async Task Stress_Receive_ByteSeq([Range(0, 2048, 1024)] int size)
        {
            var data = await Prx.OpReceiveByteSeqAsync(size);
            CollectionAssert.AreEqual(Servant.OpReceiveByteSeqData, data);
        }

        public class StressTest : Service, IStressTest
        {
            public StressTest()
            {
                OpReceiveByteSeqData = Array.Empty<byte>();
                OpSendByteSeqData = Array.Empty<byte>();
            }

            public byte[] OpReceiveByteSeqData { get; private set; }
            public byte[] OpSendByteSeqData { get; private set; }

            public ValueTask<ReadOnlyMemory<byte>> OpReceiveByteSeqAsync(
                int size,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                OpReceiveByteSeqData = Enumerable.Range(0, size).Select(x => (byte)x).ToArray();
                return new(OpReceiveByteSeqData);
            }

            public ValueTask OpSendByteSeqAsync(byte[] data, Dispatch dispatch, CancellationToken cancel)
            {
                OpSendByteSeqData = data;
                return default;
            }
        }
    }
}
