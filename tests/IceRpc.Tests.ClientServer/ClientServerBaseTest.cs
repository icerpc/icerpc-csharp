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
            : this(Protocol.Ice2, "tcp")
        {
        }

        public ClientServerBaseTest(Protocol protocol, string transport)
        {
            int basePort = 12000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.ClientServer.BasePort"))
            {
                basePort = int.Parse(TestContext.Parameters["IceRpc.Tests.ClientServer.BasePort"]!);
            }
            _basePort = Interlocked.Add(ref _nextBasePort, 25) + basePort;
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

        [SetUp]
        public async Task InitializeAsync() => await Server.ActivateAsync();

        [TearDown]
        public async Task DisposeAsync()
        {
            await Server.DisposeAsync();
            await Communicator.DisposeAsync();
        }

        public string GetTestEndpoint(
            string host = "127.0.0.1",
            int port = 0,
            string? transport = null,
            Protocol? protocol = null) =>
            (protocol ?? Protocol) == Protocol.Ice2 ?
                $"ice+{transport ?? Transport}://{host}:{GetTestPort(port)}" :
                $"{transport ?? Transport} -h {host} -p {GetTestPort(port)}";

        public int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(
            string identity,
            string host = "127.0.0.1",
            int port = 0,
            string? transport = null,
            Protocol? protocol = null) =>
            (protocol ?? Protocol) == Protocol.Ice2 ?
                $"ice+{transport ?? Transport}://{host}:{GetTestPort(port)}/{identity}" :
                $"{identity}:{transport ?? Transport} -h {host} -p {GetTestPort(port)}";
    }
}
