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
    [TestFixture(Protocol.Ice1, "ws")]
    [TestFixture(Protocol.Ice2, "tcp")]
    [TestFixture(Protocol.Ice2, "ws")]
    [Parallelizable(ParallelScope.All)]
    [Timeout(10000)]
    public class StressTests : ClientServerBaseTest
    {
        private Communicator Communicator { get; }
        private Server Server { get; }
        private Protocol Protocol { get; }
        private string Transport { get; }
        private IStressTestServicePrx Prx { get; }
        private TestService Servant { get; }

        public StressTests(Protocol protocol, string transport)
        {
            Protocol = protocol;
            Transport = transport;
            Communicator = new Communicator();
            Server = new Server(
                Communicator,
                new ServerOptions()
                {
                    Endpoints = GetTestEndpoint(protocol: Protocol, transport: Transport),
                    ColocationScope = ColocationScope.None
                });
            Servant = new TestService();
            Prx = Server.AddWithUUID(Servant, IStressTestServicePrx.Factory);
        }

        [SetUp]
        public async Task InitializeAsync() => await Server.ActivateAsync();

        [TearDown]
        public async Task DisposeAsync()
        {
            await Server.DisposeAsync();
            await Communicator.DisposeAsync();
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
