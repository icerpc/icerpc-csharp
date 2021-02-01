// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ZeroC.Ice.Discovery;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Discovery
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper, int num)
        {
            TextWriter output = helper.Output;
            Communicator communicator = helper.Communicator;

            var proxies = new List<IControllerPrx>();
            var facetedProxies = new List<IControllerPrx>();
            var indirectProxies = new List<IControllerPrx>();
            bool ice1 = helper.Protocol == Protocol.Ice1;

            for (int i = 0; i < num; ++i)
            {
                proxies.Add(IControllerPrx.Parse(ice1 ? $"controller{i}" : $"ice:controller{i}", communicator));

                facetedProxies.Add(IControllerPrx.Parse(
                    ice1 ? $"faceted-controller{i} -f abc" : $"ice:faceted-controller{i}#abc",
                    communicator));

                indirectProxies.Add(
                    IControllerPrx.Parse(ice1 ? $"controller{i} @ control{i}" : $"ice:control{i}//controller{i}",
                                         communicator));
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

            output.Write("testing faceted well-known proxies... ");
            output.Flush();
            {
                foreach (IControllerPrx prx in facetedProxies)
                {
                    await prx.IcePingAsync();
                }
            }
            output.WriteLine("ok");

            output.Write("testing object adapter registration... ");
            output.Flush();
            {
                try
                {
                    await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (NoEndpointException)
                {
                }

                proxies[0].ActivateObjectAdapter("oa", "oa1", "");

                try
                {
                    await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (ObjectNotExistException)
                {
                }

                proxies[0].DeactivateObjectAdapter("oa");

                try
                {
                    await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (NoEndpointException)
                {
                }
            }
            output.WriteLine("ok");

            output.Write("testing object adapter migration... ");
            output.Flush();
            {
                proxies[0].ActivateObjectAdapter("oa", "oa1", "");
                proxies[0].AddObject("oa", "object");
                await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                proxies[0].RemoveObject("oa", "object");
                proxies[0].DeactivateObjectAdapter("oa");

                proxies[1].ActivateObjectAdapter("oa", "oa1", "");
                proxies[1].AddObject("oa", "object");
                await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                proxies[1].RemoveObject("oa", "object");
                proxies[1].DeactivateObjectAdapter("oa");
            }
            output.WriteLine("ok");

            output.Write("testing object migration... ");
            output.Flush();
            {
                proxies[0].ActivateObjectAdapter("oa", "oa1", "");
                proxies[1].ActivateObjectAdapter("oa", "oa2", "");

                proxies[0].AddObject("oa", "object");
                await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                await IObjectPrx.Parse(ice1 ? "object" : "ice:object", communicator).IcePingAsync();
                proxies[0].RemoveObject("oa", "object");

                proxies[1].AddObject("oa", "object");
                await IObjectPrx.Parse(ice1 ? "object @ oa2" : "ice:oa2//object", communicator).IcePingAsync();
                await IObjectPrx.Parse(ice1 ? "object" : "ice:object", communicator).IcePingAsync();
                proxies[1].RemoveObject("oa", "object");

                try
                {
                    await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                }
                catch (ObjectNotExistException)
                {
                }
                try
                {
                    await IObjectPrx.Parse(ice1 ? "object @ oa2" : "ice:oa2//object", communicator).IcePingAsync();
                }
                catch (ObjectNotExistException)
                {
                }

                proxies[0].DeactivateObjectAdapter("oa");
                proxies[1].DeactivateObjectAdapter("oa");
            }
            output.WriteLine("ok");

            output.Write("testing replica groups... ");
            output.Flush();
            {
                proxies[0].ActivateObjectAdapter("oa", "oa1", "rg");
                proxies[1].ActivateObjectAdapter("oa", "oa2", "rg");
                proxies[2].ActivateObjectAdapter("oa", "oa3", "rg");

                proxies[0].AddObject("oa", "object");
                proxies[1].AddObject("oa", "object");
                proxies[2].AddObject("oa", "object");

                await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                await IObjectPrx.Parse(ice1 ? "object @ oa2" : "ice:oa2//object", communicator).IcePingAsync();
                await IObjectPrx.Parse(ice1 ? "object @ oa3" : "ice:oa3//object", communicator).IcePingAsync();

                await IObjectPrx.Parse(ice1 ? "object @ rg" : "ice:rg//object", communicator).IcePingAsync();

                var adapterIds = new List<string>
                {
                    "oa1",
                    "oa2",
                    "oa3"
                };

                // Check that the well known object is reachable with all replica group members
                ITestIntfPrx intf = ITestIntfPrx.Parse(ice1 ? "object" : "ice:object", communicator).Clone(
                    cacheConnection: false,
                    preferExistingConnection: false,
                    locatorCacheTimeout: TimeSpan.Zero);
                while (adapterIds.Count > 0)
                {
                    string id = intf.GetAdapterId();
                    adapterIds.Remove(id);
                    switch (id)
                    {
                        case "oa1":
                        {
                            proxies[0].DeactivateObjectAdapter("oa");
                            break;
                        }
                        case "oa2":
                        {
                            proxies[1].DeactivateObjectAdapter("oa");
                            break;
                        }
                        case "oa3":
                        {
                            proxies[2].DeactivateObjectAdapter("oa");
                            break;
                        }
                    }
                }

                proxies[0].ActivateObjectAdapter("oa", "oa1", "rg");
                proxies[1].ActivateObjectAdapter("oa", "oa2", "rg");
                proxies[2].ActivateObjectAdapter("oa", "oa3", "rg");

                proxies[0].AddObject("oa", "object");
                proxies[1].AddObject("oa", "object");
                proxies[2].AddObject("oa", "object");

                await IObjectPrx.Parse(ice1 ? "object @ oa1" : "ice:oa1//object", communicator).IcePingAsync();
                await IObjectPrx.Parse(ice1 ? "object @ oa2" : "ice:oa2//object", communicator).IcePingAsync();
                await IObjectPrx.Parse(ice1 ? "object @ oa3" : "ice:oa3//object", communicator).IcePingAsync();

                await IObjectPrx.Parse(ice1 ? "object @ rg" : "ice:rg//object", communicator).IcePingAsync();

                adapterIds = new List<string>
                {
                    "oa1",
                    "oa2",
                    "oa3"
                };

                // Check that the indirect reference is reachable with all replica group members
                intf = ITestIntfPrx.Parse(ice1 ? "object @ rg" : "ice:rg//object", communicator).Clone(
                    cacheConnection: false,
                    preferExistingConnection: false,
                    locatorCacheTimeout: TimeSpan.Zero);
                while (adapterIds.Count > 0)
                {
                    var id = intf.GetAdapterId();
                    adapterIds.Remove(id);
                    switch (id)
                    {
                        case "oa1":
                        {
                            proxies[0].DeactivateObjectAdapter("oa");
                            break;
                        }
                        case "oa2":
                        {
                            proxies[1].DeactivateObjectAdapter("oa");
                            break;
                        }
                        case "oa3":
                        {
                            proxies[2].DeactivateObjectAdapter("oa");
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
                var discoveryServerOptions = new DiscoveryServerOptions
                {
                    DomainId = communicator.GetProperty("Ice.Discovery.DomainId") ?? "",
                    Lookup = communicator.GetProperty("Ice.Discovery.Lookup") ?? "",
                    MulticastEndpoints = communicator.GetProperty("Ice.Discovery.Multicast.Endpoints") ?? "",
                    RetryCount = communicator.GetPropertyAsInt("Ice.Discovery.RetryCount") ?? 20,
                    ReplyServerName = communicator.GetProperty("Ice.Discovery.Reply.ServerName") ?? "",
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
                    TestHelper.Assert(comm.DefaultLocator != null);

                    discoveryServerOptions.Lookup = $"udp -h {multicast} --interface unknown"; // invalid value
                    await using var discoveryServer = new DiscoveryServer(comm, discoveryServerOptions);

                    try
                    {
                        await IObjectPrx.Parse(ice1 ? "controller0@control0" : "ice:control0//controller0", comm).
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
                    TestHelper.Assert(comm.DefaultLocator != null);

                    string port = $"{helper.BasePort + 10}";
                    string intf = helper.Host.Contains(":") ? $"\"{helper.Host}\"" : helper.Host;
                    discoveryServerOptions.Lookup =
                        $"udp -h {multicast} --interface unknown:udp -h {multicast} -p {port}"; // partially bad
                    if (!OperatingSystem.IsLinux())
                    {
                        discoveryServerOptions.Lookup += $" --interface {intf}";
                    }

                    await using var discoveryServer = new DiscoveryServer(comm, discoveryServerOptions);

                    await IObjectPrx.Parse(ice1 ? "controller0@control0" : "ice:control0//controller0", comm).
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
