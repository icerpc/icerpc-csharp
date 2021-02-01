
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Ice.Tests
{
    public class FunctionalTest
    {
        public Communicator Communicator { get; }
        public ObjectAdapter ObjectAdapter { get; }
        public Protocol Protocol { get; }
        public string Transport { get; }

        // Base port for the tests that run with this test fixture
        private int _basePort;
        private static int _nextBasePort = 12000;

        public FunctionalTest(Protocol protocol, string transport)
        {
            _basePort = Interlocked.Add(ref _nextBasePort, 100);
            TestContext.WriteLine($"BasePort: {_basePort} Protocol: {protocol}");
            Protocol = protocol;
            Transport = transport;
            Communicator = new Communicator();
            ObjectAdapter = Communicator.CreateObjectAdapterWithEndpoints("TestAdapter", GetTestEndpoint());
        }
        
        [OneTimeTearDown]
        public async Task DisposeAsync() => await Communicator.DisposeAsync();

        public string GetTestEndpoint(int port = 0)
        {
            if (Protocol == Protocol.Ice2)
            {
                var sb = new StringBuilder("ice+");
                sb.Append(Transport);
                sb.Append("://localhost:");
                sb.Append(GetTestPort(port));
                return sb.ToString();
            }
            else
            {
                var sb = new StringBuilder(Transport);
                sb.Append(" -h localhost ");
                sb.Append(" -p ");
                sb.Append(GetTestPort(port));
                return sb.ToString();
            }
        }

        public int GetTestPort(int num) => _basePort + num;

        public string GetTestProxy(string identity, int port = 0)
        {
            if (Protocol == Protocol.Ice2)
            {
                var sb = new StringBuilder("ice+");
                sb.Append(Transport);
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
                sb.Append(Transport);
                sb.Append(" -h localhost ");
                sb.Append(" -p ");
                sb.Append(GetTestPort(port));
                return sb.ToString();
            }
        }
    }
}