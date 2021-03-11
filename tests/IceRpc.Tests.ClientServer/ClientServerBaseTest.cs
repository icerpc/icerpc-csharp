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
            int basePort = 12000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.ClientServer.BasePort"))
            {
                basePort = int.Parse(TestContext.Parameters["IceRpc.Tests.ClientServer.BasePort"]!);
            }
            _basePort = Interlocked.Add(ref _nextBasePort, 15) + basePort;
        }

        public static string EscapeIPv6Address(string address, Protocol protocol = Protocol.Ice2) =>
            protocol switch
            {
                Protocol.Ice1 => IsIPv6(address) ? $"\"{address}\"" : address,
                _ => IsIPv6(address) ? $"[{address}]" : address,
            };

        public string GetTestEndpoint(
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            Protocol protocol = Protocol.Ice2) =>
            protocol == Protocol.Ice2 ?
                $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{GetTestPort(port)}" :
                $"{transport} -h {EscapeIPv6Address(host, protocol)} -p {GetTestPort(port)}";

        public int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(
            string identity,
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            Protocol protocol = Protocol.Ice2) =>
            protocol == Protocol.Ice2 ?
                $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{GetTestPort(port)}/{identity}" :
                $"{identity}:{transport} -h {EscapeIPv6Address(host, protocol)} -p {GetTestPort(port)}";

        public static bool IsIPv6(string address) => address.Contains(":");
    }
}
