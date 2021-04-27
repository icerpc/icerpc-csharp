// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Binding
{
    public static class AllTests
    {
        private static async Task<ITestIntfPrx> CreateTestIntfPrxAsync(List<IRemoteServerPrx> servers)
        {
            var endpoints = new List<string>();
            ITestIntfPrx? obj = null;
            IEnumerator<IRemoteServerPrx> p = servers.GetEnumerator();
            while (p.MoveNext())
            {
                obj = await p.Current.GetTestIntfAsync();
                endpoints.Add(obj.Endpoint);
                endpoints.AddRange(obj.AltEndpoints);
            }
            TestHelper.Assert(obj != null);
            obj.Endpoint = endpoints[0];
            obj.AltEndpoints = endpoints.Skip(1);
            obj.IsOneway = false;
            return obj.AddTlsFalse();
        }

        private static async Task DeactivateAsync(IRemoteCommunicatorPrx communicator, List<IRemoteServerPrx> servers)
        {
            IEnumerator<IRemoteServerPrx> p = servers.GetEnumerator();
            while (p.MoveNext())
            {
                await communicator.DeactivateServerAsync(p.Current);
            }
        }

        // If this proxy has an ice+tcp or ice+ws endpoint, add ?tls=false
        private static ITestIntfPrx AddTlsFalse(this ITestIntfPrx prx)
        {
            if (prx.Protocol == Protocol.Ice2)
            {
                if (prx.Endpoint.Length > 0 &&
                    prx.Endpoint.StartsWith("ice+tcp:") || prx.Endpoint.StartsWith("ice+ws:"))
                {
                    prx.Endpoint = $"{prx.Endpoint}?tls=false";
                }

                prx.AltEndpoints = prx.AltEndpoints.Select(e =>
                {
                    if (e.StartsWith("ice+tcp:") || e.StartsWith("ice+ws:"))
                    {
                        e = $"{e}?tls=false";
                    }
                    return e;
                });
            }
            return prx;
        }

        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;
            bool ice1 = helper.Protocol == Protocol.Ice1;

            var com = IRemoteCommunicatorPrx.Parse(helper.GetTestProxy("communicator", 0), communicator);
            string testTransport = helper.Transport;

            var rand = new Random(unchecked((int)DateTime.Now.Ticks));
            System.IO.TextWriter output = helper.Output;

            output.Write("testing binding with single endpoint... ");
            output.Flush();
            {
                // Use "default" with ice1 + tcp here to ensure that it still works
                IRemoteServerPrx? server = await com.CreateServerAsync(
                    "Adapter",
                    (ice1 && testTransport == "tcp") ? "default" : testTransport);
                TestHelper.Assert(server != null);
                ITestIntfPrx? test1 = (await server.GetTestIntfAsync())?.AddTlsFalse();
                ITestIntfPrx? test2 = (await server.GetTestIntfAsync())?.AddTlsFalse();
                TestHelper.Assert(test1 != null && test2 != null);
                TestHelper.Assert(await test1.GetConnectionAsync() == await test2.GetConnectionAsync());

                await test1.IcePingAsync();
                await test2.IcePingAsync();

                await com.DeactivateServerAsync(server);

                var test3 = test1.As<ITestIntfPrx>();
                TestHelper.Assert(test3.Connection == test1.Connection);

                try
                {
                    await test3.IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (ConnectFailedException)
                {
                }
            }
            output.WriteLine("ok");

            output.Write("testing binding with multiple endpoints... ");
            output.Flush();
            {
                var servers = new List<IRemoteServerPrx>
                {
                    await com.CreateServerAsync("Adapter31", testTransport)!,
                    await com.CreateServerAsync("Adapter32", testTransport)!,
                    await com.CreateServerAsync("Adapter33", testTransport)!
                };

                ITestIntfPrx obj = await CreateTestIntfPrxAsync(servers);

                // Ensure that endpoints are tried in order by deactivating the servers one after the other.
                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter31");
                }
                await com.DeactivateServerAsync(servers[0]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter32");
                }
                await com.DeactivateServerAsync(servers[1]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter33");
                }
                await com.DeactivateServerAsync(servers[2]);

                try
                {
                    await obj.GetAdapterNameAsync();
                }
                catch (ConnectFailedException)
                {
                }
                servers.Clear();
            }
            output.WriteLine("ok");

            output.Write("testing per request binding with single endpoint... ");
            output.Flush();
            {
                IRemoteServerPrx? server = await com.CreateServerAsync("Adapter41", testTransport);
                TestHelper.Assert(server != null);
                ITestIntfPrx test1 = (await server.GetTestIntfAsync())!.AddTlsFalse();
                test1.CacheConnection = false;
                test1.PreferExistingConnection = false;

                ITestIntfPrx test2 = (await server.GetTestIntfAsync())!.AddTlsFalse();
                test2.CacheConnection = false;
                test2.PreferExistingConnection = false;

                TestHelper.Assert(!test1.CacheConnection && !test1.PreferExistingConnection);
                TestHelper.Assert(!test2.CacheConnection && !test2.PreferExistingConnection);
                TestHelper.Assert(await test1.GetConnectionAsync() == await test2.GetConnectionAsync());

                await test1.IcePingAsync();

                await com.DeactivateServerAsync(server);

                var test3 = test1.As<ITestIntfPrx>();
                try
                {
                    TestHelper.Assert(await test3.GetConnectionAsync() == await test1.GetConnectionAsync());
                    TestHelper.Assert(false);
                }
                catch (ConnectFailedException)
                {
                }
            }
            output.WriteLine("ok");

            output.Write("testing per request binding with multiple endpoints... ");
            output.Flush();
            {
                var servers = new List<IRemoteServerPrx>
                {
                    await com.CreateServerAsync("Adapter61", testTransport)!,
                    await com.CreateServerAsync("Adapter62", testTransport)!,
                    await com.CreateServerAsync("Adapter63", testTransport)!
                };

                ITestIntfPrx obj = await CreateTestIntfPrxAsync(servers);
                obj.CacheConnection = false;
                obj.PreferExistingConnection = false;
                TestHelper.Assert(!obj.CacheConnection && !obj.PreferExistingConnection);

                // Ensure that endpoints are tried in order by deactivating the servers one after the other.
                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter61");
                }
                await com.DeactivateServerAsync(servers[0]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter62");
                }
                await com.DeactivateServerAsync(servers[1]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter63");
                }
                await com.DeactivateServerAsync(servers[2]);

                try
                {
                    await obj.GetAdapterNameAsync();
                }
                catch (ConnectFailedException)
                {
                }

                string endpoint = obj.Endpoint;
                string[] altEndpoints = obj.AltEndpoints.ToArray();
                servers.Clear();

                // TODO: ice1-only for now, because we send the client endpoints for use in Server configuration.
                if (helper.Protocol == Protocol.Ice1)
                {
                    // Now, re-activate the servers with the same endpoints in the opposite order.
                    // Wait 5 seconds to let recent endpoint failures expire
                    Thread.Sleep(5000);
                    servers.Add(await com.CreateServerWithEndpointsAsync("Adapter66", altEndpoints[1]));
                    for (int i = 0; i < 3; i++)
                    {
                        TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter66");
                    }

                    // Wait 5 seconds to let recent endpoint failures expire
                    Thread.Sleep(5000);
                    servers.Add(await com.CreateServerWithEndpointsAsync("Adapter65", altEndpoints[0]));
                    for (int i = 0; i < 3; i++)
                    {
                        TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter65");
                    }

                    // Wait 5 seconds to let recent endpoint failures expire
                    Thread.Sleep(5000);
                    servers.Add(await com.CreateServerWithEndpointsAsync("Adapter64", endpoint));
                    for (int i = 0; i < 3; i++)
                    {
                        TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter64");
                    }

                    await DeactivateAsync(com, servers);
                }
            }
            output.WriteLine("ok");

            output.Write("testing connection reuse with multiple endpoints... ");
            output.Flush();
            {
                var servers1 = new List<IRemoteServerPrx>
                {
                    await com.CreateServerAsync("Adapter81", testTransport)!,
                    await com.CreateServerAsync("Adapter82", testTransport)!,
                    await com.CreateServerAsync("Adapter83", testTransport)!
                };

                var servers2 = new List<IRemoteServerPrx>
                {
                    servers1[0],
                    await com.CreateServerAsync("Adapter84", testTransport)!,
                    await com.CreateServerAsync("Adapter85", testTransport)!
                };

                ITestIntfPrx obj1 = await CreateTestIntfPrxAsync(servers1);
                ITestIntfPrx obj2 = await CreateTestIntfPrxAsync(servers2);

                await com.DeactivateServerAsync(servers1[0]);

                Task<string> t1 = obj1.GetAdapterNameAsync();
                Task<string> t2 = obj2.GetAdapterNameAsync();
                TestHelper.Assert(t1.Result == "Adapter82");
                TestHelper.Assert(t2.Result == "Adapter84");

                await DeactivateAsync(com, servers1);
                await DeactivateAsync(com, servers2);
            }

            {
                var servers1 = new List<IRemoteServerPrx>
                {
                    await com.CreateServerAsync("Adapter91", testTransport)!,
                    await com.CreateServerAsync("Adapter92", testTransport)!,
                    await com.CreateServerAsync("Adapter93", testTransport)!
                };

                var servers2 = new List<IRemoteServerPrx>
                {
                    servers1[0],
                    await com.CreateServerAsync("Adapter94", testTransport)!,
                    await com.CreateServerAsync("Adapter95", testTransport)!
                };

                ITestIntfPrx obj1 = await CreateTestIntfPrxAsync(servers1);
                ITestIntfPrx obj2 = await CreateTestIntfPrxAsync(servers2);

                Task<string> t1 = obj1.GetAdapterNameAsync();
                Task<string> t2 = obj2.GetAdapterNameAsync();
                TestHelper.Assert(t1.Result == "Adapter91");
                TestHelper.Assert(t2.Result == "Adapter91");

                await DeactivateAsync(com, servers1);
                await DeactivateAsync(com, servers2);
            }
            output.WriteLine("ok");

            if (helper.Protocol == Protocol.Ice1)
            {
                // Not clear what we're testing here
                /*
                output.Write("testing endpoint mode filtering... ");
                output.Flush();
                {
                    var servers = new List<IRemoteServerPrx>
                    {
                        await com.CreateServerAsync("Adapter72", "udp"),
                        await com.CreateServerAsync("Adapter71", testTransport),
                    };

                    ITestIntfPrx obj = await CreateTestIntfPrxAsync(servers);
                    TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter71"));

                    servers.RemoveAt(servers.Count - 1);
                    ITestIntfPrx testUDP = await CreateTestIntfPrxAsync(servers);
                    testUDP.IsOneway = true;

                    try
                    {
                        await testUDP.GetConnectionAsync();
                        TestHelper.Assert(false);
                    }
                    catch (NoEndpointException)
                    {
                        // expected
                    }

                    try
                    {
                        testUDP.GetAdapterName();
                        TestHelper.Assert(false);
                    }
                    catch (NoEndpointException)
                    {
                        // expected
                    }
                }
                output.WriteLine("ok");
                */
            }
            if (communicator.GetProperty("Ice.Plugin.IceSSL") != null)
            {
                output.Write("testing secure and non-secure endpoints... ");
                output.Flush();
                {
                    var servers = new List<IRemoteServerPrx>
                    {
                        await com.CreateServerAsync("Adapter81", "ssl")!,
                        await com.CreateServerAsync("Adapter82", "tcp")!
                    };

                    ITestIntfPrx obj = await CreateTestIntfPrxAsync(servers);

                    for (int i = 0; i < 5; i++)
                    {
                        TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter82");
                        _ = (await obj.GetConnectionAsync()).GoAwayAsync();
                    }

                    await com.DeactivateServerAsync(servers[1]);

                    for (int i = 0; i < 5; i++)
                    {
                        TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter81");
                        _ = (await obj.GetConnectionAsync()).GoAwayAsync();
                    }

                    // TODO: ice1-only for now, because we send the client endpoints for use in Server configuration.
                    if (helper.Protocol == Protocol.Ice1)
                    {
                        await com.CreateServerWithEndpointsAsync("Adapter83", obj.AltEndpoints.ToArray()[0]); // Recreate a tcp Server.

                        for (int i = 0; i < 5; i++)
                        {
                            TestHelper.Assert(await obj.GetAdapterNameAsync() == "Adapter83");
                            _ = (await obj.GetConnectionAsync()).GoAwayAsync();
                        }
                    }
                    await DeactivateAsync(com, servers);
                }
                output.WriteLine("ok");
            }

            {
                output.Write("testing ipv4 & ipv6 connections... ");
                output.Flush();

                Func<string, string> getEndpoint = host =>
                    TestHelper.GetTestEndpoint(
                        new Dictionary<string, string>(communicator.GetProperties())
                        {
                            ["Test.Host"] = host
                        },
                        2,
                        "tcp");

                Func<string, string, string> getProxy = (identity, host) =>
                    TestHelper.GetTestProxy(
                        identity,
                        new Dictionary<string, string>(communicator.GetProperties())
                        {
                            ["Test.Host"] = host
                        },
                        2,
                        "tcp");

                /*
                This sounds like a totally pointeless test.

                var anyipv4 = new ServerOptions
                {
                    HasColocEndpoint = false,
                    Endpoints = getEndpoint("0.0.0.0"),
                    PublishedEndpoints = getEndpoint("127.0.0.1")
                };

                var anyipv6 = new ServerOptions
                {
                    HasColocEndpoint = false,
                    Endpoints = getEndpoint("::0"),
                    PublishedEndpoints = getEndpoint("::1")
                };

                var anyipv46 = new ServerOptions
                {
                    HasColocEndpoint = false,
                    Endpoints = getEndpoint("::0"),
                    PublishedEndpoints = getEndpoint("127.0.0.1")
                };

                var anylocalhost = new ServerOptions
                {
                    HasColocEndpoint = false,
                    Endpoints = getEndpoint("::0"),
                    PublishedEndpoints = getEndpoint("localhost")
                };

                var localipv4 = new ServerOptions
                {
                    HasColocEndpoint = false,
                    Endpoints = getEndpoint("127.0.0.1"),
                    PublishedHost = "127.0.0.1"
                };

                var localipv6 = new ServerOptions
                {
                    HasColocEndpoint = false,
                    Endpoints = getEndpoint("::1"),
                    PublishedHost = "::1"
                };

                var serverOptions = new ServerOptions[]
                {
                    anyipv4,
                    anyipv6,
                    anyipv46,
                    anylocalhost,
                    localipv4,
                    localipv6
                };

                foreach (ServerOptions p in serverOptions)
                {
                    await using var serverCommunicator = new Communicator();
                    await using var oa = new Server(serverCommunicator, p);
                    oa.Listen();

                    IServicePrx prx = IServicePrx.Factory.Create(oa, "/dummy");
                    try
                    {
                        await using var clientCommunicator = new Communicator();
                        prx = IServicePrx.Parse(prx.ToString()!, clientCommunicator);
                        await prx.IcePingAsync();
                        TestHelper.Assert(false);
                    }
                    catch (ServiceNotFoundException)
                    {
                        // Expected. Server is reachable but there's no "dummy" object
                    }
                }

                */

                // Test IPv6 dual mode socket
                {
                    await using var serverCommunicator = new Communicator();
                    string endpoint = getEndpoint("::0");
                    await using var oa = new Server
                    {
                        HasColocEndpoint = false,
                        Communicator = serverCommunicator,
                        Endpoint = endpoint
                    };
                    oa.Listen();

                    Console.Out.Flush();

                    try
                    {
                        await using var ipv4Server = new Server
                        {
                            Communicator = serverCommunicator,
                            Endpoint = getEndpoint("0.0.0.0")
                        };

                        ipv4Server.Listen();
                        TestHelper.Assert(false);
                    }
                    catch (TransportException)
                    {
                        // Expected. ::0 is a dual-mode socket so binding 0.0.0.0 will fail
                    }

                    try
                    {
                        await using var clientCommunicator = new Communicator();
                        var prx = IServicePrx.Parse(getProxy("dummy", "127.0.0.1"), clientCommunicator);
                        await prx.IcePingAsync();
                    }
                    catch (ServiceNotFoundException)
                    {
                        // Expected, no object registered.
                    }
                }

                // Test IPv6 only
                {
                    await using var serverCommunicator = new Communicator();
                    string endpoint = getEndpoint("::0");
                    await using var oa = new Server
                    {
                        HasColocEndpoint = false,
                        Communicator = serverCommunicator,
                        Endpoint = endpoint,
                        ConnectionOptions = new()
                        {
                            TransportOptions = new TcpOptions()
                            {
                                IsIPv6Only = true
                            }
                        }
                    };

                    oa.Listen();

                    // 0.0.0.0 can still be bound if ::0 is IPv6 only
                    {
                        string ipv4Endpoint = getEndpoint("0.0.0.0");
                        await using var ipv4Server = new Server
                        {
                            HasColocEndpoint = false,
                            Communicator = serverCommunicator,
                            Endpoint = ipv4Endpoint
                        };

                        ipv4Server.Listen();
                    }

                    try
                    {
                        await using var clientCommunicator = new Communicator();
                        var prx = IServicePrx.Parse(getProxy("dummy", "127.0.0.1"), clientCommunicator);
                        await prx.IcePingAsync();
                        TestHelper.Assert(false);
                    }
                    catch (ConnectionRefusedException)
                    {
                        // Expected, server socket is IPv6 only.
                    }
                }

                // Listen on IPv4 loopback with IPv6 dual mode socket
                {
                    await using var serverCommunicator = new Communicator();
                    string endpoint = getEndpoint("::ffff:127.0.0.1");
                    await using var oa = new Server
                    {
                        HasColocEndpoint = false,
                        Communicator = serverCommunicator,
                        Endpoint = endpoint
                    };
                    oa.Listen();

                    try
                    {
                        string ipv4Endpoint = getEndpoint("127.0.0.1");
                        await using var ipv4Server = new Server
                        {
                            HasColocEndpoint = false,
                            Communicator = serverCommunicator,
                            Endpoint = ipv4Endpoint
                        };
                        ipv4Server.Listen();
                        TestHelper.Assert(false);
                    }
                    catch (TransportException)
                    {
                        // Expected. 127.0.0.1 is already in use
                    }

                    try
                    {
                        await using var clientCommunicator = new Communicator();
                        var prx = IServicePrx.Parse(getProxy("dummy", "127.0.0.1"), clientCommunicator);
                        await prx.IcePingAsync();
                    }
                    catch (ServiceNotFoundException)
                    {
                        // Expected, no object registered.
                    }
                }

                output.WriteLine("ok");
            }

            await com.ShutdownAsync();
        }
    }
}
