// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(5000)]
    public class StressTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly StressTest _service = new();
        private readonly IStressTestPrx _prx;

        public StressTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => _service)
                .UseTransport("tcp")
                .UseProtocol(protocol)
                .AddTransient(_ => new Configure.ConnectionOptions { IncomingFrameMaxSize = 2048 * 1024 })
                .BuildServiceProvider();
            _prx = _serviceProvider.GetProxy<StressTestPrx>();
        }

        [TearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [TestCase(0)]
        [TestCase(1024)]
        [TestCase(32 * 1024)]
        [TestCase(64 * 1024)]
        [TestCase(1024 * 1024)]
        public async Task Stress_Send_ByteSeq(int size)
        {
            byte[] data = new byte[size];
            await _prx.OpSendByteSeqAsync(data);
            Assert.That(_service.OpSendByteSeqData, Is.EqualTo(data));
        }

        [TestCase(0)]
        [TestCase(1024)]
        [TestCase(32 * 1024)]
        [TestCase(64 * 1024)]
        [TestCase(1024 * 1024)]
        public async Task Stress_Receive_ByteSeq(int size)
        {
            byte[] data = await _prx.OpReceiveByteSeqAsync(size);
            Assert.That(_service.OpReceiveByteSeqData, Is.EqualTo(data));
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
