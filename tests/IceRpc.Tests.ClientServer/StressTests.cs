// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture(Protocol.Ice1, "tcp")]
    [TestFixture(Protocol.Ice2, "tcp")]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class StressTests : ClientServerBaseTest
    {
        private Connection Connection { get; }
        private Server Server { get; }
        private Protocol Protocol { get; }
        private string TransportCode { get; }
        private IStressTestPrx Prx { get; }
        private StressTest Servant { get; }

        public StressTests(Protocol protocol, string transport)
        {
            Protocol = protocol;
            TransportCode = transport;
            Servant = new StressTest();
            Server = new Server
            {
                Dispatcher = Servant,
                Endpoint = GetTestEndpoint(protocol: Protocol, transport: TransportCode),
            };
            Connection = new Connection { RemoteEndpoint = Server.Endpoint };
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
