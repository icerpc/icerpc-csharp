
using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ZeroC.Ice;

namespace IceRPC.Ice.Tests
{
    public class FunctionalTest : IAsyncLifetime
    {
        public Communicator Communicator { get; }

        private static int _nextBasePort = 12000;
        // Base port for the tests that run with this test fixture
        private static int _basePort;

        public FunctionalTest()
        {
            _basePort = Interlocked.Add(ref _nextBasePort, 100);
            Communicator = new Communicator();
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public async Task DisposeAsync() => await Communicator.DisposeAsync();

        public string GetTestEndpoint(
            Protocol protocol,
            string transport,
            int port = 0)
        {
            if (protocol == Protocol.Ice2)
            {
                var sb = new StringBuilder("ice+");
                sb.Append(transport);
                sb.Append("://localhost:");
                sb.Append(GetTestPort(port));
                return sb.ToString();
            }
            else
            {
                var sb = new StringBuilder(transport);
                sb.Append(" -h localhost ");
                sb.Append(" -p ");
                sb.Append(GetTestPort(port));
                return sb.ToString();
            }
        }

        int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(
            Protocol protocol,
            string transport,
            string identity,
            int port = 0)
        {
            if (protocol == Protocol.Ice2)
            {
                var sb = new StringBuilder("ice+");
                sb.Append(transport);
                sb.Append("://localhost:");
                sb.Append(GetTestPort(port));
                sb.Append('/');
                sb.Append(identity);
                return sb.ToString();
            }
            else // i.e. ice1
            {
                var sb = new StringBuilder(identity);
                sb.Append(':');
                sb.Append(transport);
                sb.Append(" -h localhost ");
                sb.Append(" -p ");
                sb.Append(GetTestPort(port));
                return sb.ToString();
            }
        }
    }
}