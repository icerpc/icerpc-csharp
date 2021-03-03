// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(scope: ParallelScope.All)]
    [TestFixture(Protocol.Ice1, "tcp")]
    [TestFixture(Protocol.Ice1, "ws")]
    [TestFixture(Protocol.Ice2, "tcp")]
    [TestFixture(Protocol.Ice2, "ws")]
    public class StressTests : ClientServerBaseTest
    {
        private IStressTestServicePrx Prx { get; }
        public TestService Servant { get; }

        public StressTests(Protocol protocol, string transport)
            : base(protocol, transport)
        {
            Servant = new TestService();
            Prx = Server.AddWithUUID(Servant, IStressTestServicePrx.Factory);
        }

        [Test]
        public async Task Stress_Send_ByteSeq([Range(0, 65536, 9601)] int size)
        {
            var data = Enumerable.Range(0, size).Select(x => (byte)x).ToArray();
            await Prx.OpSendByteSeqAsync(data);
            CollectionAssert.AreEqual(data, Servant.OpSendByteSeqData);
        }

        [Test]
        public async Task Stress_Receive_ByteSeq([Range(0, 65536, 9601)] int size)
        {
            var data = await Prx.OpReceiveByteSeqAsync(size);
            CollectionAssert.AreEqual(Servant.OpReceiveByteSeqData, data);
        }

        public class TestService : IAsyncStressTestService
        {
            public TestService()
            {
                OpReceiveByteSeqData = Array.Empty<byte>();
                OpSendByteSeqData = Array.Empty<byte>();
            }

            public byte[] OpReceiveByteSeqData { get; private set; }
            public byte[] OpSendByteSeqData { get; private set; }

            public ValueTask<ReadOnlyMemory<byte>> OpReceiveByteSeqAsync(
                int size,
                Current current,
                CancellationToken cancel)
            {
                OpReceiveByteSeqData = Enumerable.Range(0, size).Select(x => (byte)x).ToArray();
                return new(OpReceiveByteSeqData);
            }

            public ValueTask OpSendByteSeqAsync(byte[] data, Current current, CancellationToken cancel)
            {
                OpSendByteSeqData = data;
                return default;
            }
        }
    }
}
