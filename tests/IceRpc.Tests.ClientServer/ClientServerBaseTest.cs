// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Globalization;

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
                basePort = int.Parse(TestContext.Parameters["IceRpc.Tests.ClientServer.BasePort"]!,
                                     CultureInfo.InvariantCulture.NumberFormat);
            }
            _basePort = Interlocked.Add(ref _nextBasePort, 15) + basePort;
        }

        public Endpoint GetTestEndpoint(
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            bool tls = false,
            Protocol? protocol = null) =>
            TestHelper.GetTestEndpoint(host, GetTestPort(port), transport, tls, protocol);

        public int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(
            string path,
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            bool tls = false,
            Protocol? protocol = null) =>
            TestHelper.GetTestProxy(path, host, GetTestPort(port), transport, tls, protocol);
    }
}
