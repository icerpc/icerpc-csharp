// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Binding
{
    public static class AllTests
    {
        private static ITestIntfPrx CreateTestIntfPrx(List<IRemoteObjectAdapterPrx> adapters)
        {
            var endpoints = new List<Endpoint>();
            ITestIntfPrx? obj = null;
            IEnumerator<IRemoteObjectAdapterPrx> p = adapters.GetEnumerator();
            while (p.MoveNext())
            {
                obj = p.Current.GetTestIntf();
                endpoints.AddRange(obj!.Endpoints);
            }
            TestHelper.Assert(obj != null);
            return obj.Clone(endpoints: endpoints, oneway: false);
        }

        private static void Deactivate(IRemoteCommunicatorPrx communicator, List<IRemoteObjectAdapterPrx> adapters)
        {
            IEnumerator<IRemoteObjectAdapterPrx> p = adapters.GetEnumerator();
            while (p.MoveNext())
            {
                communicator.DeactivateObjectAdapter(p.Current);
            }
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
                IRemoteObjectAdapterPrx? adapter = await com.CreateObjectAdapterAsync(
                    "Adapter",
                    (ice1 && testTransport == "tcp") ? "default" : testTransport);
                TestHelper.Assert(adapter != null);
                ITestIntfPrx? test1 = adapter.GetTestIntf();
                ITestIntfPrx? test2 = adapter.GetTestIntf();
                TestHelper.Assert(test1 != null && test2 != null);
                TestHelper.Assert(await test1.GetConnectionAsync() == await test2.GetConnectionAsync());

                await test1.IcePingAsync();
                await test2.IcePingAsync();

                com.DeactivateObjectAdapter(adapter);

                var test3 = test1.Clone(ITestIntfPrx.Factory);
                TestHelper.Assert(test3.GetCachedConnection() == test1.GetCachedConnection());
                TestHelper.Assert(test3.GetCachedConnection() == test2.GetCachedConnection());

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
                var adapters = new List<IRemoteObjectAdapterPrx>
                {
                    await com.CreateObjectAdapterAsync("Adapter31", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter32", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter33", testTransport)!
                };

                ITestIntfPrx obj = CreateTestIntfPrx(adapters);

                // Ensure that endpoints are tried in order by deactivating the adapters one after the other.
                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(obj.GetAdapterName() == "Adapter31");
                }
                com.DeactivateObjectAdapter(adapters[0]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(obj.GetAdapterName() == "Adapter32");
                }
                com.DeactivateObjectAdapter(adapters[1]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(obj.GetAdapterName() == "Adapter33");
                }
                com.DeactivateObjectAdapter(adapters[2]);

                try
                {
                    obj.GetAdapterName();
                }
                catch (ConnectFailedException)
                {
                }
                adapters.Clear();
            }
            output.WriteLine("ok");

            output.Write("testing per request binding with single endpoint... ");
            output.Flush();
            {
                IRemoteObjectAdapterPrx? adapter = await com.CreateObjectAdapterAsync("Adapter41", testTransport);
                TestHelper.Assert(adapter != null);
                ITestIntfPrx test1 = adapter.GetTestIntf()!.Clone(cacheConnection: false,
                                                                  preferExistingConnection: false);
                ITestIntfPrx test2 = adapter.GetTestIntf()!.Clone(cacheConnection: false,
                                                                  preferExistingConnection: false);
                TestHelper.Assert(!test1.CacheConnection && !test1.PreferExistingConnection);
                TestHelper.Assert(!test2.CacheConnection && !test2.PreferExistingConnection);
                TestHelper.Assert(await test1.GetConnectionAsync() == await test2.GetConnectionAsync());

                await test1.IcePingAsync();

                com.DeactivateObjectAdapter(adapter);

                var test3 = test1.Clone(ITestIntfPrx.Factory);
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
                var adapters = new List<IRemoteObjectAdapterPrx>
                {
                    await com.CreateObjectAdapterAsync("Adapter61", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter62", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter63", testTransport)!
                };

                ITestIntfPrx obj = CreateTestIntfPrx(adapters);
                obj = obj.Clone(cacheConnection: false, preferExistingConnection: false);
                TestHelper.Assert(!obj.CacheConnection && !obj.PreferExistingConnection);

                // Ensure that endpoints are tried in order by deactivating the adapters one after the other.
                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(obj.GetAdapterName() == "Adapter61");
                }
                com.DeactivateObjectAdapter(adapters[0]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(obj.GetAdapterName() == "Adapter62");
                }
                com.DeactivateObjectAdapter(adapters[1]);

                for (int i = 0; i < 3; i++)
                {
                    TestHelper.Assert(obj.GetAdapterName() == "Adapter63");
                }
                com.DeactivateObjectAdapter(adapters[2]);

                try
                {
                    obj.GetAdapterName();
                }
                catch (ConnectFailedException)
                {
                }

                IReadOnlyList<Endpoint> endpoints = obj.Endpoints;
                adapters.Clear();

                // TODO: ice1-only for now, because we send the client endpoints for use in OA configuration.
                if (helper.Protocol == Protocol.Ice1)
                {
                    // Now, re-activate the adapters with the same endpoints in the opposite order.
                    // Wait 5 seconds to let recent endpoint failures expire
                    Thread.Sleep(5000);
                    adapters.Add(com.CreateObjectAdapterWithEndpoints("Adapter66", endpoints[2].ToString()));
                    for (int i = 0; i < 3; i++)
                    {
                        TestHelper.Assert(obj.GetAdapterName() == "Adapter66");
                    }

                    // Wait 5 seconds to let recent endpoint failures expire
                    Thread.Sleep(5000);
                    adapters.Add(com.CreateObjectAdapterWithEndpoints("Adapter65", endpoints[1].ToString()));
                    for (int i = 0; i < 3; i++)
                    {
                        TestHelper.Assert(obj.GetAdapterName() == "Adapter65");
                    }

                    // Wait 5 seconds to let recent endpoint failures expire
                    Thread.Sleep(5000);
                    adapters.Add(com.CreateObjectAdapterWithEndpoints("Adapter64", endpoints[0].ToString()));
                    for (int i = 0; i < 3; i++)
                    {
                        TestHelper.Assert(obj.GetAdapterName() == "Adapter64");
                    }

                    Deactivate(com, adapters);
                }
            }
            output.WriteLine("ok");

            output.Write("testing connection reuse with multiple endpoints... ");
            output.Flush();
            {
                var adapters1 = new List<IRemoteObjectAdapterPrx>
                {
                    await com.CreateObjectAdapterAsync("Adapter81", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter82", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter83", testTransport)!
                };

                var adapters2 = new List<IRemoteObjectAdapterPrx>
                {
                    adapters1[0],
                    await com.CreateObjectAdapterAsync("Adapter84", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter85", testTransport)!
                };

                ITestIntfPrx obj1 = CreateTestIntfPrx(adapters1);
                ITestIntfPrx obj2 = CreateTestIntfPrx(adapters2);

                com.DeactivateObjectAdapter(adapters1[0]);

                Task<string> t1 = obj1.GetAdapterNameAsync();
                Task<string> t2 = obj2.GetAdapterNameAsync();
                TestHelper.Assert(t1.Result == "Adapter82");
                TestHelper.Assert(t2.Result == "Adapter84");

                Deactivate(com, adapters1);
                Deactivate(com, adapters2);
            }

            {
                var adapters1 = new List<IRemoteObjectAdapterPrx>
                {
                    await com.CreateObjectAdapterAsync("Adapter91", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter92", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter93", testTransport)!
                };

                var adapters2 = new List<IRemoteObjectAdapterPrx>
                {
                    adapters1[0],
                    await com.CreateObjectAdapterAsync("Adapter94", testTransport)!,
                    await com.CreateObjectAdapterAsync("Adapter95", testTransport)!
                };

                ITestIntfPrx obj1 = CreateTestIntfPrx(adapters1);
                ITestIntfPrx obj2 = CreateTestIntfPrx(adapters2);

                Task<string> t1 = obj1.GetAdapterNameAsync();
                Task<string> t2 = obj2.GetAdapterNameAsync();
                TestHelper.Assert(t1.Result == "Adapter91");
                TestHelper.Assert(t2.Result == "Adapter91");

                Deactivate(com, adapters1);
                Deactivate(com, adapters2);
            }
            output.WriteLine("ok");

            if (helper.Protocol == Protocol.Ice1)
            {
                output.Write("testing endpoint mode filtering... ");
                output.Flush();
                {
                    var adapters = new List<IRemoteObjectAdapterPrx>
                    {
                        await com.CreateObjectAdapterAsync("Adapter72", "udp"),
                        await com.CreateObjectAdapterAsync("Adapter71", testTransport),
                    };

                    ITestIntfPrx obj = CreateTestIntfPrx(adapters);
                    TestHelper.Assert(obj.GetAdapterName().Equals("Adapter71"));

                    adapters.RemoveAt(adapters.Count - 1);
                    ITestIntfPrx testUDP = CreateTestIntfPrx(adapters).Clone(oneway: true);

                    // test that datagram proxies fail if PreferNonSecure is false
                    testUDP = testUDP.Clone(preferNonSecure: NonSecure.Never);
                    try
                    {
                        await testUDP.GetConnectionAsync();
                        TestHelper.Assert(false);
                    }
                    catch (NoEndpointException)
                    {
                        // expected
                    }

                    testUDP = testUDP.Clone(preferNonSecure: NonSecure.Always);
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
            }
            if (communicator.GetProperty("Ice.Plugin.IceSSL") != null)
            {
                output.Write("testing secure and non-secure endpoints... ");
                output.Flush();
                {
                    var adapters = new List<IRemoteObjectAdapterPrx>
                    {
                        await com.CreateObjectAdapterAsync("Adapter81", "ssl")!,
                        await com.CreateObjectAdapterAsync("Adapter82", "tcp")!
                    };

                    ITestIntfPrx obj = CreateTestIntfPrx(adapters);

                    for (int i = 0; i < 5; i++)
                    {
                        TestHelper.Assert(obj.GetAdapterName().Equals("Adapter82"));
                        _ = (await obj.GetConnectionAsync()).GoAwayAsync();
                    }

                    ITestIntfPrx testNonSecure = obj.Clone(preferNonSecure: NonSecure.Always);
                    // TODO: update when PreferNonSecure default is updated
                    ITestIntfPrx testSecure = obj.Clone(preferNonSecure: NonSecure.Never);
                    TestHelper.Assert(await obj.GetConnectionAsync() != await testSecure.GetConnectionAsync());
                    TestHelper.Assert(await obj.GetConnectionAsync() == await testNonSecure.GetConnectionAsync());

                    com.DeactivateObjectAdapter(adapters[1]);

                    for (int i = 0; i < 5; i++)
                    {
                        TestHelper.Assert(obj.GetAdapterName().Equals("Adapter81"));
                        _ = (await obj.GetConnectionAsync()).GoAwayAsync();
                    }

                    // TODO: ice1-only for now, because we send the client endpoints for use in OA configuration.
                    if (helper.Protocol == Protocol.Ice1)
                    {
                        com.CreateObjectAdapterWithEndpoints("Adapter83", obj.Endpoints[1].ToString()); // Recreate a tcp OA.

                        for (int i = 0; i < 5; i++)
                        {
                            TestHelper.Assert(obj.GetAdapterName().Equals("Adapter83"));
                            _ = (await obj.GetConnectionAsync()).GoAwayAsync();
                        }
                    }

                    com.DeactivateObjectAdapter(adapters[0]);

                    try
                    {
                        await testSecure.IcePingAsync();
                        TestHelper.Assert(false);
                    }
                    catch (ConnectionRefusedException)
                    {
                        // expected
                    }
                    Deactivate(com, adapters);
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

                var anyipv4 = new ObjectAdapterOptions
                {
                    Endpoints = getEndpoint("0.0.0.0"),
                    PublishedEndpoints = getEndpoint("127.0.0.1")
                };

                var anyipv6 = new ObjectAdapterOptions
                {
                    Endpoints = getEndpoint("::0"),
                    PublishedEndpoints = getEndpoint("::1")
                };

                var anyipv46 = new ObjectAdapterOptions
                {
                    Endpoints = getEndpoint("::0"),
                    PublishedEndpoints = getEndpoint("127.0.0.1")
                };

                var anylocalhost = new ObjectAdapterOptions
                {
                    Endpoints = getEndpoint("::0"),
                    PublishedEndpoints = getEndpoint("localhost")
                };

                var localipv4 = new ObjectAdapterOptions
                {
                    Endpoints = getEndpoint("127.0.0.1"),
                    ServerName = "127.0.0.1"
                };

                var localipv6 = new ObjectAdapterOptions
                {
                    Endpoints = getEndpoint("::1"),
                    ServerName = "::1"
                };

                var localhost = new ObjectAdapterOptions
                {
                    Endpoints = getEndpoint("localhost"),
                    ServerName = "localhost"
                };

                var serverOptions = new ObjectAdapterOptions[]
                {
                    anyipv4,
                    anyipv6,
                    anyipv46,
                    anylocalhost,
                    localipv4,
                    localipv6,
                    localhost
                };

                foreach (ObjectAdapterOptions p in serverOptions)
                {
                    await using var serverCommunicator = new Communicator();
                    await using var oa = new ObjectAdapter(serverCommunicator, p);
                    await oa.ActivateAsync();

                    IServicePrx prx = oa.CreateProxy("dummy", IServicePrx.Factory);
                    try
                    {
                        await using var clientCommunicator = new Communicator();
                        prx = IServicePrx.Parse(prx.ToString()!, clientCommunicator);
                        await prx.IcePingAsync();
                        TestHelper.Assert(false);
                    }
                    catch (ObjectNotExistException)
                    {
                        // Expected. OA is reachable but there's no "dummy" object
                    }
                }

                // Test IPv6 dual mode socket
                {
                    await using var serverCommunicator = new Communicator();
                    string endpoint = getEndpoint("::0");
                    await using var oa = new ObjectAdapter(
                        serverCommunicator,
                        new() { Endpoints = endpoint });
                    await oa.ActivateAsync();

                    try
                    {
                        await using var ipv4OA = new ObjectAdapter(
                            serverCommunicator,
                            new() { Endpoints = getEndpoint("0.0.0.0") });
                        await ipv4OA.ActivateAsync();
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
                    catch (ObjectNotExistException)
                    {
                        // Expected, no object registered.
                    }
                }

                // Test IPv6 only endpoints
                {
                    await using var serverCommunicator = new Communicator();
                    string endpoint = getEndpoint("::0") + (ice1 ? " --ipv6Only" : "?ipv6-only=true");
                    await using var oa = new ObjectAdapter(
                        serverCommunicator,
                        new() { Endpoints = endpoint });
                    await oa.ActivateAsync();

                    // 0.0.0.0 can still be bound if ::0 is IPv6 only
                    {
                        string ipv4Endpoint = getEndpoint("0.0.0.0");
                        await using var ipv4OA = new ObjectAdapter(
                                serverCommunicator,
                                new() { Endpoints = ipv4Endpoint });
                        await ipv4OA.ActivateAsync();
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
                    await using var oa = new ObjectAdapter(
                        serverCommunicator,
                        new() { Endpoints = endpoint });
                    await oa.ActivateAsync();

                    try
                    {
                        string ipv4Endpoint = getEndpoint("127.0.0.1");
                        await using var ipv4OA = new ObjectAdapter(
                            serverCommunicator,
                            new() { Endpoints = ipv4Endpoint });
                        await ipv4OA.ActivateAsync();
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
                    catch (ObjectNotExistException)
                    {
                        // Expected, no object registered.
                    }
                }

                output.WriteLine("ok");
            }

            com.Shutdown();
        }
    }
}
