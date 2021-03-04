// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IceRpc.Interop.ZeroC.Ice.Discovery;
using IceRpc.Test;

namespace IceRpc.Test.Discovery
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper, int num)
        {
            TextWriter output = helper.Output;
            Communicator communicator = helper.Communicator;

            // TODO: convert properties to options for now
            var discoveryServerOptions = new DiscoveryServerOptions
            {
                ColocationScope = ColocationScope.Communicator,
                DomainId = communicator.GetProperty("Ice.Discovery.DomainId") ?? "",
                Lookup = communicator.GetProperty("Ice.Discovery.Lookup") ?? "",
                MulticastEndpoints = communicator.GetProperty("Ice.Discovery.Multicast.Endpoints") ?? "",
                RetryCount = communicator.GetPropertyAsInt("Ice.Discovery.RetryCount") ?? 20,
                ReplyPublishedHost = communicator.GetProperty("Ice.Discovery.Reply.PublishedHost") ?? "",
                Timeout = communicator.GetPropertyAsTimeSpan("Ice.Discovery.Timeout") ?? TimeSpan.FromMilliseconds(100)
            };

            await using var discoveryServer = new DiscoveryServer(communicator, discoveryServerOptions);
            communicator.DefaultLocationService = new LocationService(discoveryServer.Locator);
            await discoveryServer.ActivateAsync();

            var proxies = new List<IControllerPrx>();
            var indirectProxies = new List<IControllerPrx>();

            for (int i = 0; i < num; ++i)
            {
                proxies.Add(IControllerPrx.Parse($"controller{i}", communicator));

                indirectProxies.Add(IControllerPrx.Parse($"controller{i} @ control{i}", communicator));
            }

            output.Write("testing indirect proxies... ");
            output.Flush();
            {
                foreach (IControllerPrx prx in indirectProxies)
                {
                    await prx.IcePingAsync();
                }
            }
            output.WriteLine("ok");

            output.Write("testing well-known proxies... ");
            output.Flush();
            {
                foreach (IControllerPrx prx in proxies)
                {
                    await prx.IcePingAsync();
                }
            }
            output.WriteLine("ok");

            output.Write("testing server registration... ");
            output.Flush();
            {
                try
                {
                    await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (NoEndpointException)
                {
                }

                proxies[0].ActivateServer("oa", "oa1", "");

                try
                {
                    await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (ServiceNotFoundException)
                {
                }

                proxies[0].DeactivateServer("oa");

                try
                {
                    await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (NoEndpointException)
                {
                }
            }
            output.WriteLine("ok");

            output.Write("testing server migration... ");
            output.Flush();
            {
                proxies[0].ActivateServer("oa", "oa1", "");
                proxies[0].AddObject("oa", "object");
                await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                proxies[0].RemoveObject("oa", "object");
                proxies[0].DeactivateServer("oa");

                proxies[1].ActivateServer("oa", "oa1", "");
                proxies[1].AddObject("oa", "object");
                await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                proxies[1].RemoveObject("oa", "object");
                proxies[1].DeactivateServer("oa");
            }
            output.WriteLine("ok");

            output.Write("testing object migration... ");
            output.Flush();
            {
                proxies[0].ActivateServer("oa", "oa1", "");
                proxies[1].ActivateServer("oa", "oa2", "");

                proxies[0].AddObject("oa", "object");
                await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                await IServicePrx.Parse("object", communicator).IcePingAsync();
                proxies[0].RemoveObject("oa", "object");

                proxies[1].AddObject("oa", "object");
                await IServicePrx.Parse("object @ oa2", communicator).IcePingAsync();
                await IServicePrx.Parse("object", communicator).IcePingAsync();
                proxies[1].RemoveObject("oa", "object");

                try
                {
                    await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                }
                catch (ServiceNotFoundException)
                {
                }
                try
                {
                    await IServicePrx.Parse("object @ oa2", communicator).IcePingAsync();
                }
                catch (ServiceNotFoundException)
                {
                }

                proxies[0].DeactivateServer("oa");
                proxies[1].DeactivateServer("oa");
            }
            output.WriteLine("ok");

            output.Write("testing replica groups... ");
            output.Flush();
            {
                proxies[0].ActivateServer("oa", "oa1", "rg");
                proxies[1].ActivateServer("oa", "oa2", "rg");
                proxies[2].ActivateServer("oa", "oa3", "rg");

                proxies[0].AddObject("oa", "object");
                proxies[1].AddObject("oa", "object");
                proxies[2].AddObject("oa", "object");

                await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                await IServicePrx.Parse("object @ oa2", communicator).IcePingAsync();
                await IServicePrx.Parse("object @ oa3", communicator).IcePingAsync();

                await IServicePrx.Parse("object @ rg", communicator).IcePingAsync();

                var adapterIds = new List<string>
                {
                    "oa1",
                    "oa2",
                    "oa3"
                };

                // Replace location service to change the cache ttl.
                communicator.DefaultLocationService =
                    new LocationService(discoveryServer.Locator, new() { Ttl = TimeSpan.Zero });

                // Check that the well known object is reachable with all replica group members
                ITestIntfPrx intf = ITestIntfPrx.Parse("object", communicator).Clone(
                    cacheConnection: false,
                    preferExistingConnection: false);
                while (adapterIds.Count > 0)
                {
                    string id = intf.GetAdapterId();
                    adapterIds.Remove(id);
                    switch (id)
                    {
                        case "oa1":
                        {
                            proxies[0].DeactivateServer("oa");
                            break;
                        }
                        case "oa2":
                        {
                            proxies[1].DeactivateServer("oa");
                            break;
                        }
                        case "oa3":
                        {
                            proxies[2].DeactivateServer("oa");
                            break;
                        }
                    }
                }

                proxies[0].ActivateServer("oa", "oa1", "rg");
                proxies[1].ActivateServer("oa", "oa2", "rg");
                proxies[2].ActivateServer("oa", "oa3", "rg");

                proxies[0].AddObject("oa", "object");
                proxies[1].AddObject("oa", "object");
                proxies[2].AddObject("oa", "object");

                await IServicePrx.Parse("object @ oa1", communicator).IcePingAsync();
                await IServicePrx.Parse("object @ oa2", communicator).IcePingAsync();
                await IServicePrx.Parse("object @ oa3", communicator).IcePingAsync();

                await IServicePrx.Parse("object @ rg", communicator).IcePingAsync();

                adapterIds = new List<string>
                {
                    "oa1",
                    "oa2",
                    "oa3"
                };

                // Check that the indirect reference is reachable with all replica group members
                intf = ITestIntfPrx.Parse("object @ rg", communicator).Clone(
                    cacheConnection: false,
                    preferExistingConnection: false);
                while (adapterIds.Count > 0)
                {
                    var id = intf.GetAdapterId();
                    adapterIds.Remove(id);
                    switch (id)
                    {
                        case "oa1":
                        {
                            proxies[0].DeactivateServer("oa");
                            break;
                        }
                        case "oa2":
                        {
                            proxies[1].DeactivateServer("oa");
                            break;
                        }
                        case "oa3":
                        {
                            proxies[2].DeactivateServer("oa");
                            break;
                        }
                    }
                }
            }
            output.WriteLine("ok");

            output.Write("testing invalid lookup endpoints... ");
            output.Flush();
            {
                // TODO: convert properties to options for now
                discoveryServerOptions = new DiscoveryServerOptions
                {
                    ColocationScope = ColocationScope.Communicator,
                    DomainId = communicator.GetProperty("Ice.Discovery.DomainId") ?? "",
                    Lookup = communicator.GetProperty("Ice.Discovery.Lookup") ?? "",
                    MulticastEndpoints = communicator.GetProperty("Ice.Discovery.Multicast.Endpoints") ?? "",
                    RetryCount = communicator.GetPropertyAsInt("Ice.Discovery.RetryCount") ?? 20,
                    ReplyPublishedHost = communicator.GetProperty("Ice.Discovery.Reply.PublishedHost") ?? "",
                    Timeout = communicator.GetPropertyAsTimeSpan("Ice.Discovery.Timeout") ?? TimeSpan.FromMilliseconds(100)
                };

                string multicast;
                if (helper.Host.Contains(":"))
                {
                    multicast = "\"ff15::1\"";
                }
                else
                {
                    multicast = "239.255.0.1";
                }

                {
                    await using var comm = new Communicator(communicator.GetProperties());

                    discoveryServerOptions.Lookup = $"udp -h {multicast} --interface unknown"; // invalid value
                    await using var discoveryServer2 = new DiscoveryServer(comm, discoveryServerOptions);
                    comm.DefaultLocationService = new LocationService(discoveryServer2.Locator);

                    try
                    {
                        // relies on ColocationScope = Communicator
                        await IServicePrx.Parse("controller0@control0", comm).
                            IcePingAsync();
                        TestHelper.Assert(false);
                    }
                    catch
                    {
                        // expected
                    }
                }
                {
                    await using var comm = new Communicator(communicator.GetProperties());

                    string port = $"{helper.BasePort + 10}";
                    string intf = helper.Host.Contains(":") ? $"\"{helper.Host}\"" : helper.Host;
                    discoveryServerOptions.Lookup =
                        $"udp -h {multicast} --interface unknown:udp -h {multicast} -p {port}"; // partially bad
                    if (!OperatingSystem.IsLinux())
                    {
                        discoveryServerOptions.Lookup += $" --interface {intf}";
                    }

                    await using var discoveryServer2 = new DiscoveryServer(comm, discoveryServerOptions);
                    comm.DefaultLocationService = new LocationService(discoveryServer2.Locator);

                    await IServicePrx.Parse("controller0@control0", comm).
                        IcePingAsync();
                }
            }
            output.WriteLine("ok");

            output.Write("shutting down... ");
            output.Flush();
            foreach (IControllerPrx prx in proxies)
            {
                await prx.ShutdownAsync();
            }
            output.WriteLine("ok");
        }
    }
}
