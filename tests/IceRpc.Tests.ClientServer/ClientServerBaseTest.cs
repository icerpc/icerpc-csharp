// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    public class ClientServerBaseTest
    {
        public Communicator Communicator { get; }
        public Server Server { get; }
        public Protocol Protocol { get; }
        public string Transport { get; }

        // Base port for the tests that run with this test fixture
        private readonly int _basePort;
        private static int _nextBasePort;

        public ClientServerBaseTest()
            : this(Protocol.Ice2, "")
        {
        }

        public ClientServerBaseTest(Protocol protocol, string transport)
        {
            int basePort = 12000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.ClientServer.BasePort"))
            {
                basePort = int.Parse(TestContext.Parameters["IceRpc.Tests.ClientServer.BasePort"]!);
            }
            _basePort = Interlocked.Add(ref _nextBasePort, 100) + basePort;
            Protocol = protocol;
            Transport = transport;
            Communicator = new Communicator();
            Server = new(
                Communicator,
                new()
                {
                    Endpoints = GetTestEndpoint(),
                    ColocationScope = ColocationScope.None
                });
        }

        [OneTimeSetUp]
        public async Task InitializeAsync() => await Server.ActivateAsync();

        [OneTimeTearDown]
        public async Task DisposeAsync()
        {
            await Server.DisposeAsync();
            await Communicator.DisposeAsync();
        }

        public string GetTestEndpoint(int port = 0) =>
            Protocol == Protocol.Ice2 ?
                $"ice+{Transport}://localhost:{GetTestPort(port)}" :
                $"{Transport} -h localhost -p {GetTestPort(port)}";

        public int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(string identity, int port = 0) =>
            Protocol == Protocol.Ice2 ?
                $"ice+{Transport}://localhost:{GetTestPort(port)}/{identity}" :
                $"{identity}:{Transport} -h localhost -p {GetTestPort(port)}";
    }
}
