// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using NUnit.Framework;
namespace IceRpc.Tests.ClientServer
{
    public class ClientServerBaseTest
    {
        // Base port for the tests that run with this test fixture
        private readonly int _basePort;
        private static int _nextBasePort;

        public ClientServerBaseTest()
        {
            int basePort = 13000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.ClientServer.BasePort"))
            {
                basePort = int.Parse(TestContext.Parameters["IceRpc.Tests.ClientServer.BasePort"]!);
            }
            _basePort = Interlocked.Add(ref _nextBasePort, 25) + basePort;
        }

        public string GetTestEndpoint(
            string host = "127.0.0.1",
            int port = 0,
            string? transport = null,
            Protocol? protocol = null) =>
            (protocol ?? Protocol.Ice2) == Protocol.Ice2 ?
                $"ice+{transport ?? "tcp"}://{host}:{GetTestPort(port)}" :
                $"{transport ?? "tcp"} -h {host} -p {GetTestPort(port)}";

        public int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(
            string identity,
            string host = "127.0.0.1",
            int port = 0,
            string? transport = null,
            Protocol? protocol = null) =>
            (protocol ?? Protocol.Ice2) == Protocol.Ice2 ?
                $"ice+{transport ?? "tcp"}://{host}:{GetTestPort(port)}/{identity}" :
                $"{identity}:{transport ?? "tcp"} -h {host} -p {GetTestPort(port)}";
    }
}
