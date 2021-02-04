// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests
{
    public class FunctionalTest
    {
        public Communicator Communicator { get; }
        public string DefaultHost { get; }
        public string DefaultTransport { get; }
        public ObjectAdapter ObjectAdapter { get; }
        public Protocol Protocol { get; }
        public string Transport { get; }

        // Base port for the tests that run with this test fixture
        private int _basePort;
        private static int _nextBasePort = 0;

        public FunctionalTest() : this(Protocol.Ice2, "")
        {
        }

        public FunctionalTest(Protocol protocol, string transport)
        {
            int basePort = 12000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.BasePort"))
            {
                basePort = int.Parse(TestContext.Parameters["IceRpc.Tests.BasePort"]!);
            }
            _basePort = Interlocked.Add(ref _nextBasePort, 100) + basePort;
            Protocol = protocol;

            DefaultHost = "localhost";
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.DefaultHost"))
            {
                DefaultHost = TestContext.Parameters["IceRpc.Tests.DefaultHost"]!;
            }

            DefaultTransport = "tcp";
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.DefaultTransport"))
            {
                DefaultTransport = TestContext.Parameters["IceRpc.Tests.DefaultTransport"]!;
            }
            Transport = transport.Length == 0 ? DefaultTransport : transport;
            Communicator = new Communicator();
            ObjectAdapter = Communicator.CreateObjectAdapter(
                "TestAdapter-0",
                new ObjectAdapterOptions()
                {
                    Endpoints = GetTestEndpoint()
                });
        }

        [OneTimeTearDown]
        public Task DisposeAsync() => Communicator.DestroyAsync();

        public string GetTestEndpoint(int port = 0) =>
            Protocol == Protocol.Ice2 ?
                $"ice+{Transport}://{DefaultHost}:{GetTestPort(port)}" :
                $"{Transport} -h localhost -p {GetTestPort(port)}";

        public int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(string identity, int port = 0) =>
            Protocol == Protocol.Ice2 ?
                $"ice+{Transport}://{DefaultHost}:{GetTestPort(port)}/{identity}" :
                $"{identity}:{Transport} -h localhost -p {GetTestPort(port)}";
    }
}
