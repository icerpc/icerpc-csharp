// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Info
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;

            bool ice1 = helper.Protocol == Protocol.Ice1;
            string transport = helper.Transport;
            TextWriter output = helper.Output;

            Server server;
            output.Write("test server endpoint information... ");
            output.Flush();
            {
                string publishedHost = helper.Host;
                server = new Server(
                    communicator,
                    new()
                    {
                        AcceptNonSecure = NonSecure.Always,
                        Endpoints = $"tcp -h \"{helper.Host}\" -p 0 -t 15000",
                        PublishedHost = publishedHost
                    });

                IReadOnlyList<Endpoint> endpoints = server.Endpoints;
                TestHelper.Assert(endpoints.Count == 1);
                IReadOnlyList<Endpoint> publishedEndpoints = server.PublishedEndpoints;
                TestHelper.Assert(endpoints.SequenceEqual(publishedEndpoints));

                Endpoint tcpEndpoint = endpoints[0];
                TestHelper.Assert(tcpEndpoint != null);
                TestHelper.Assert(tcpEndpoint.Transport == Transport.TCP);

                TestHelper.Assert(tcpEndpoint.Host == publishedHost);
                TestHelper.Assert(tcpEndpoint.Port > 0);
                TestHelper.Assert(tcpEndpoint["timeout"] is string value && int.Parse(value) == 15000);
                await server.DisposeAsync();

                int port = helper.BasePort + 1;

                server = new Server(
                    communicator,
                    new()
                    {
                        Endpoints = ice1 ? $"{transport} -h 0.0.0.0 -p {port}" : $"ice+{transport}://0.0.0.0:{port}",
                        PublishedEndpoints = helper.GetTestEndpoint(1)
                    });

                endpoints = server.Endpoints;
                TestHelper.Assert(endpoints.Count >= 1);
                publishedEndpoints = server.PublishedEndpoints;
                TestHelper.Assert(publishedEndpoints.Count == 1);

                foreach (Endpoint endpoint in endpoints)
                {
                    TestHelper.Assert(endpoint.Port == port);
                }

                tcpEndpoint = publishedEndpoints[0];
                TestHelper.Assert(tcpEndpoint.Host == "127.0.0.1");
                TestHelper.Assert(tcpEndpoint.Port == port);

                await server.DisposeAsync();
            }
            output.WriteLine("ok");

            int endpointPort = helper.BasePort + 0;

            ITestIntfPrx testIntf;
            if (ice1)
            {
                testIntf = ITestIntfPrx.Parse("test:" + helper.GetTestEndpoint(0) + ":" +
                                              helper.GetTestEndpoint(0, "udp"), communicator);
            }
            else
            {
                testIntf = ITestIntfPrx.Parse(helper.GetTestProxy("test", 0), communicator);
            }

            string defaultHost = helper.Host;

            output.Write("test connection endpoint information... ");
            output.Flush();
            {
                Endpoint tcpEndpoint = (await testIntf.GetConnectionAsync()).Endpoint;
                TestHelper.Assert(tcpEndpoint.Port == endpointPort);

                TestHelper.Assert(tcpEndpoint["compress"] == null);
                TestHelper.Assert(tcpEndpoint.Host == defaultHost);

                SortedDictionary<string, string> ctx = testIntf.GetEndpointInfoAsContext();
                TestHelper.Assert(ctx["host"] == tcpEndpoint.Host);
                TestHelper.Assert(ctx["compress"] == "false");
                int port = int.Parse(ctx["port"]);
                TestHelper.Assert(port > 0);

                if (ice1)
                {
                    Endpoint udpEndpoint =
                        (await testIntf.Clone(oneway: true,
                                              preferNonSecure: NonSecure.Always).GetConnectionAsync()).Endpoint;
                    TestHelper.Assert(udpEndpoint.Port == endpointPort);
                    TestHelper.Assert(udpEndpoint.Host == defaultHost);
                }
            }
            output.WriteLine("ok");

            output.Write("testing connection information... ");
            output.Flush();
            {
                var connection = (IPConnection)await testIntf.GetConnectionAsync();

                TestHelper.Assert(!connection.IsIncoming);
                TestHelper.Assert(connection.Server == null);
                TestHelper.Assert(connection.RemoteEndpoint!.Port == endpointPort);
                TestHelper.Assert(connection.LocalEndpoint!.Port > 0);
                if (defaultHost.Equals("127.0.0.1"))
                {
                    TestHelper.Assert(connection.LocalEndpoint!.Address.ToString() == defaultHost);
                    TestHelper.Assert(connection.RemoteEndpoint!.Address.ToString() == defaultHost);
                }

                // This could be either an ice1 ssl endpoint or an ice2 tcp endpoint with PreferNonSecure == False
                if (connection.Endpoint.IsAlwaysSecure || testIntf.PreferNonSecure == NonSecure.Never)
                {
                    TestHelper.Assert(((TcpConnection)connection).IsEncrypted);
                    // WSS tests run client authentication disabled for compatibility with web browser testing.
                    if (connection.Endpoint.Transport != Transport.WSS && connection.Endpoint.Transport != Transport.WS)
                    {
                        TestHelper.Assert(((TcpConnection)connection).IsMutuallyAuthenticated);
                        TestHelper.Assert(((TcpConnection)connection).LocalCertificate != null);
                    }
                    else
                    {
                        TestHelper.Assert(!((TcpConnection)connection).IsMutuallyAuthenticated);
                        TestHelper.Assert(((TcpConnection)connection).LocalCertificate == null);
                    }
                    TestHelper.Assert(((TcpConnection)connection).IsSigned);

                    TestHelper.Assert(((TcpConnection)connection).RemoteCertificate != null);
                    TestHelper.Assert(((TcpConnection)connection).NegotiatedApplicationProtocol != null);
                    TestHelper.Assert(((TcpConnection)connection).NegotiatedCipherSuite != null);
                    TestHelper.Assert(((TcpConnection)connection).SslProtocol == SslProtocols.Tls12 ||
                                      ((TcpConnection)connection).SslProtocol == SslProtocols.Tls13);
                }
                else
                {
                    TestHelper.Assert(!((TcpConnection)connection).IsEncrypted);
                    TestHelper.Assert(!((TcpConnection)connection).IsMutuallyAuthenticated);
                    TestHelper.Assert(!((TcpConnection)connection).IsSigned);
                    TestHelper.Assert(((TcpConnection)connection).LocalCertificate == null);
                    TestHelper.Assert(((TcpConnection)connection).RemoteCertificate == null);
                    TestHelper.Assert(((TcpConnection)connection).NegotiatedApplicationProtocol == null);
                    TestHelper.Assert(((TcpConnection)connection).NegotiatedCipherSuite == null);
                    TestHelper.Assert(((TcpConnection)connection).SslProtocol == null);
                }

                SortedDictionary<string, string> ctx = testIntf.GetConnectionInfoAsContext();
                TestHelper.Assert(ctx["incoming"].Equals("true"));
                TestHelper.Assert(ctx["publishedHost"].Equals("TestAdapter"));
                TestHelper.Assert(ctx["remoteAddress"].Equals(connection.LocalEndpoint!.Address.ToString()));
                TestHelper.Assert(ctx["localAddress"].Equals(connection.RemoteEndpoint!.Address.ToString()));
                TestHelper.Assert(ctx["remotePort"].Equals(connection.LocalEndpoint!.Port.ToString()));
                TestHelper.Assert(ctx["localPort"].Equals(connection.RemoteEndpoint!.Port.ToString()));

                if ((connection as WSConnection)?.Headers is IReadOnlyDictionary<string, string> headers)
                {
                    TestHelper.Assert(headers["Upgrade"].Equals("websocket"));
                    TestHelper.Assert(headers["Connection"].Equals("Upgrade"));
                    TestHelper.Assert(headers["Sec-WebSocket-Protocol"].Equals("ice.zeroc.com"));
                    TestHelper.Assert(headers["Sec-WebSocket-Accept"] != null);

                    TestHelper.Assert(ctx["ws.Upgrade"].Equals("websocket"));
                    TestHelper.Assert(ctx["ws.Connection"].Equals("Upgrade"));
                    TestHelper.Assert(ctx["ws.Sec-WebSocket-Protocol"].Equals("ice.zeroc.com"));
                    TestHelper.Assert(ctx["ws.Sec-WebSocket-Version"].Equals("13"));
                    TestHelper.Assert(ctx["ws.Sec-WebSocket-Key"] != null);
                }

                if (ice1)
                {
                    var dTest = ITestIntfPrx.Parse("test:" + helper.GetTestEndpoint(0, "udp"), communicator);

                    connection = (IPConnection)await dTest.Clone(
                        oneway: true,
                        preferNonSecure: NonSecure.Always).GetConnectionAsync();

                    var udpConnection = connection as UdpConnection;
                    TestHelper.Assert(udpConnection != null);
                    TestHelper.Assert(!udpConnection.IsIncoming);
                    TestHelper.Assert(udpConnection.Server == null);
                    TestHelper.Assert(udpConnection.LocalEndpoint?.Port > 0);
                    TestHelper.Assert(udpConnection.RemoteEndpoint?.Port == endpointPort);

                    if (defaultHost == "127.0.0.1")
                    {
                        TestHelper.Assert(udpConnection.RemoteEndpoint.Address.ToString().Equals(defaultHost));
                        TestHelper.Assert(udpConnection.LocalEndpoint.Address.ToString().Equals(defaultHost));
                    }
                }
            }
            output.WriteLine("ok");

            await testIntf.ShutdownAsync();
        }
    }
}
