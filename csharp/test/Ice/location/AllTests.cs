// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Location
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;
            TextWriter output = helper.Output;

            bool ice1 = helper.Protocol == Protocol.Ice1;
            var manager = IServerManagerPrx.Parse(helper.GetTestProxy("ServerManager", 0), communicator);
            var locator =
                ILocatorPrx.Parse(helper.GetTestProxy("locator", 0), communicator).Clone(ITestLocatorPrx.Factory);

            ILocationResolver locationResolver = new LocationResolver(locator);
            communicator.DefaultLocationResolver = locationResolver;

            var registry = locator.GetRegistry()!.Clone(ITestLocatorRegistryPrx.Factory);
            TestHelper.Assert(registry != null);

            output.Write("testing ice1 string/URI parsing... ");
            output.Flush();
            IObjectPrx base1, base2, base3, base4, base5, base6, base7;
            if (ice1)
            {
                base1 = IObjectPrx.Parse("test @ TestAdapter", communicator);
                base2 = IObjectPrx.Parse("test @ TestAdapter", communicator);
                base3 = IObjectPrx.Parse(ice1 ? "test" : "ice:test", communicator);
                base4 = IObjectPrx.Parse("ServerManager", communicator);
                base5 = IObjectPrx.Parse("test2", communicator);
                base6 = IObjectPrx.Parse("test @ ReplicatedAdapter", communicator);
                base7 = IObjectPrx.Parse("test3 -f facet", communicator);
            }
            else
            {
                base1 = IObjectPrx.Parse("ice:TestAdapter//test", communicator);
                base2 = IObjectPrx.Parse("ice:TestAdapter//test", communicator);
                base3 = IObjectPrx.Parse("ice:test", communicator);
                base4 = IObjectPrx.Parse("ice:ServerManager", communicator);
                base5 = IObjectPrx.Parse("ice:test2", communicator);
                base6 = IObjectPrx.Parse("ice:ReplicatedAdapter//test", communicator);
                base7 = IObjectPrx.Parse("ice:test3#facet", communicator);
            }
            output.WriteLine("ok");

            output.Write("testing LocationResolver... ");
            TestHelper.Assert(base1.LocationResolver == communicator.DefaultLocationResolver);
            var anotherLocationResolver =
                new LocationResolver(ILocatorPrx.Parse(ice1 ? "anotherLocator" : "ice:anotherLocator", communicator));
            base1 = base1.Clone(locationResolver: anotherLocationResolver);
            TestHelper.Assert(base1.LocationResolver == anotherLocationResolver);
            communicator.DefaultLocationResolver = null;
            base1 = IObjectPrx.Parse(ice1 ? "test @ TestAdapter" : "ice:TestAdapter//test", communicator);
            TestHelper.Assert(base1.LocationResolver == null);
            base1 = base1.Clone(locationResolver: anotherLocationResolver);
            TestHelper.Assert(base1.LocationResolver == anotherLocationResolver);
            communicator.DefaultLocationResolver = locationResolver;
            base1 = IObjectPrx.Parse(ice1 ? "test @ TestAdapter" : "ice:TestAdapter//test", communicator);
            TestHelper.Assert(base1.LocationResolver == communicator.DefaultLocationResolver);
            output.WriteLine("ok");

            output.Write("starting server... ");
            output.Flush();
            manager.StartServer();
            output.WriteLine("ok");

            output.Write("testing checked cast... ");
            output.Flush();
            var obj1 = await base1.CheckedCastAsync(ITestIntfPrx.Factory);
            TestHelper.Assert(obj1 != null);
            var obj2 = await base2.CheckedCastAsync(ITestIntfPrx.Factory);
            TestHelper.Assert(obj2 != null);
            var obj3 = await base3.CheckedCastAsync(ITestIntfPrx.Factory);
            TestHelper.Assert(obj3 != null);
            var obj4 = await base4.CheckedCastAsync(IServerManagerPrx.Factory);
            TestHelper.Assert(obj4 != null);
            var obj5 = await base5.CheckedCastAsync(ITestIntfPrx.Factory);
            TestHelper.Assert(obj5 != null);
            var obj6 = await base6.CheckedCastAsync(ITestIntfPrx.Factory);
            TestHelper.Assert(obj6 != null);
            output.WriteLine("ok");

            output.Write("testing AdapterId//id indirect proxy... ");
            output.Flush();
            obj1.Shutdown();
            manager.StartServer();
            try
            {
                await obj2.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            output.WriteLine("ok");

            output.Write("testing ReplicaGroupId//id indirect proxy... ");
            output.Flush();
            obj1.Shutdown();
            manager.StartServer();
            try
            {
                await obj6.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            output.WriteLine("ok");

            output.Write("testing identity indirect proxy... ");
            output.Flush();
            obj1.Shutdown();
            manager.StartServer();
            try
            {
                await obj3.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            try
            {
                await obj2.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            obj1.Shutdown();
            manager.StartServer();
            try
            {
                await obj2.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            try
            {
                await obj3.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            obj1.Shutdown();
            manager.StartServer();
            try
            {
                await obj2.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            obj1.Shutdown();
            manager.StartServer();
            try
            {
                await obj3.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            obj1.Shutdown();
            manager.StartServer();
            try
            {
                obj5 = await base5.CheckedCastAsync(ITestIntfPrx.Factory);
                TestHelper.Assert(obj5 != null);
                await obj5.IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            output.WriteLine("ok");

            output.Write("testing proxy with unknown identity... ");
            output.Flush();
            try
            {
                base1 = IObjectPrx.Parse(ice1 ? "unknown/unknown" : "ice:unknown/unknown", communicator);
                await base1.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            output.WriteLine("ok");

            output.Write("testing proxy with unknown adapter... ");
            output.Flush();
            try
            {
                base1 = IObjectPrx.Parse(
                    ice1 ? "test @ TestAdapterUnknown" : "ice:TestAdapterUnknown//test", communicator);
                await base1.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            output.WriteLine("ok");

            output.Write("testing location resolver TTL... ");
            output.Flush();

            var zeroResolver = new LocationResolver(locator, new() { Ttl = TimeSpan.Zero });

            IObjectPrx basencc = IObjectPrx.Parse(
                ice1 ? "test@TestAdapter" : "ice:TestAdapter//test", communicator).Clone(
                    cacheConnection: false,
                    locationResolver: zeroResolver);
            int count = locator.GetRequestCount();
            await basencc.IcePingAsync(); // No locator cache.
            TestHelper.Assert(++count == locator.GetRequestCount());
            await basencc.IcePingAsync(); // No locator cache.
            TestHelper.Assert(++count == locator.GetRequestCount());

            var twoResolver = new LocationResolver(locator, new() { Ttl = TimeSpan.FromSeconds(2) });
            basencc = basencc.Clone(locationResolver: twoResolver);
            await basencc.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());
            await basencc.IcePingAsync();
            TestHelper.Assert(count == locator.GetRequestCount());

            var oneResolver = new LocationResolver(locator, new() { Ttl = TimeSpan.FromSeconds(1) });
            basencc = basencc.Clone(locationResolver: oneResolver);
            await basencc.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());
            Thread.Sleep(1300); // 1300ms > 1s
            await basencc.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());

            basencc = basencc.Clone(locationResolver: communicator.DefaultLocationResolver); // infinite timeout
            await basencc.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());
            await basencc.IcePingAsync();
            TestHelper.Assert(count == locator.GetRequestCount());

            output.WriteLine("ok");

            output.Write("testing proxy from server... ");
            output.Flush();
            obj1 = ITestIntfPrx.Parse(ice1 ? "test@TestAdapter" : "ice:TestAdapter//test", communicator);
            IHelloPrx? hello = obj1.GetHello();
            TestHelper.Assert(hello != null);
            TestHelper.Assert(hello.Location.Count == 1 && hello.Location[0] == "TestAdapter");
            hello.SayHello();
            hello = obj1.GetReplicatedHello();
            TestHelper.Assert(hello != null);
            TestHelper.Assert(hello.Location.Count == 1 && hello.Location[0] == "ReplicatedAdapter");
            hello.SayHello();
            output.WriteLine("ok");

            output.Write("testing well-known proxy with facet... ");
            output.Flush();
            hello = IHelloPrx.Parse(ice1 ? "bonjour -f abc" : "ice:bonjour#abc", communicator);
            hello.SayHello();
            hello = IHelloPrx.Parse(ice1 ? "hello -f abc" : "ice:hello#abc", communicator);
            try
            {
                hello.SayHello();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException) // hello does not have an abc facet
            {
            }
            output.WriteLine("ok");

            output.Write("testing locator request queuing... ");
            output.Flush();
            hello = obj1.GetReplicatedHello()!.Clone(locationResolver: zeroResolver, cacheConnection: false);

            count = locator.GetRequestCount();
            await hello.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());
            var results = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                results.Add(hello.SayHelloAsync());
            }
            Task.WaitAll(results.ToArray());
            results.Clear();
            if (locator.GetRequestCount() > count + 800)
            {
                output.Write("queuing = " + (locator.GetRequestCount() - count));
            }
            TestHelper.Assert(locator.GetRequestCount() > count && locator.GetRequestCount() < count + 999);
            count = locator.GetRequestCount();
            hello = hello.Clone(location: ImmutableArray.Create("unknown"));
            for (int i = 0; i < 1000; i++)
            {
                results.Add(hello.SayHelloAsync().ContinueWith(
                    t =>
                    {
                        try
                        {
                            t.Wait();
                        }
                        catch (AggregateException ex) when (ex.InnerException is NoEndpointException)
                        {
                        }
                    },
                    TaskScheduler.Default));
            }
            Task.WaitAll(results.ToArray());
            results.Clear();
            // XXX:
            // Take into account the retries.
            TestHelper.Assert(locator.GetRequestCount() > count && locator.GetRequestCount() < count + 1999);
            if (locator.GetRequestCount() > count + 800)
            {
                output.Write("queuing = " + (locator.GetRequestCount() - count));
            }
            output.WriteLine("ok");

            output.Write("testing adapter locator cache... ");
            output.Flush();
            try
            {
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }

            RegisterAdapterEndpoints(
                registry,
                "TestAdapter3",
                replicaGroupId: "",
                ResolveLocation(locator, "TestAdapter")!);

            try
            {
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();

                RegisterAdapterEndpoints(
                    registry,
                    "TestAdapter3",
                    replicaGroupId: "",
                    IObjectPrx.Parse(helper.GetTestProxy("dummy", 99), communicator));

                await IObjectPrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            try
            {
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).Clone(
                    locationResolver: zeroResolver).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ConnectionRefusedException)
            {
            }

            try
            {
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
            }
            catch (ConnectionRefusedException)
            {
            }

            RegisterAdapterEndpoints(
                registry,
                "TestAdapter3",
                "",
                ResolveLocation(locator, "TestAdapter")!);
            try
            {
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            output.WriteLine("ok");

            output.Write("testing well-known object locator cache... ");
            output.Flush();
            registry.AddObject(IObjectPrx.Parse(
                ice1 ? "test3@TestUnknown" : "ice:TestUnknown//test3", communicator));
            try
            {
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            registry.AddObject(IObjectPrx.Parse(
                ice1 ? "test3@TestAdapter4" : "ice:TestAdapter4//test3", communicator)); // Update
            RegisterAdapterEndpoints(
                registry,
                "TestAdapter4",
                "",
                IObjectPrx.Parse(helper.GetTestProxy("dummy", 99), communicator));
            try
            {
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ConnectionRefusedException)
            {
            }
            RegisterAdapterEndpoints(
                registry,
                "TestAdapter4",
                "",
                ResolveLocation(locator, "TestAdapter")!);
            try
            {
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            RegisterAdapterEndpoints(
                registry,
                "TestAdapter4",
                "",
                IObjectPrx.Parse(helper.GetTestProxy("dummy", 99), communicator));
            try
            {
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            try
            {
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter4" : "ice:TestAdapter4//test", communicator).Clone(
                    locationResolver: zeroResolver).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ConnectionRefusedException)
            {
            }
            try
            {
                var prx = IObjectPrx.Parse(ice1 ? "test@TestAdapter4" : "ice:TestAdapter4//test", communicator);
                communicator.DefaultLocationResolver.ClearCache(prx.Location, prx.Protocol);
                await prx.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ConnectionRefusedException)
            {
            }
            try
            {
                var prx = IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", communicator);
                communicator.DefaultLocationResolver.ClearCache(prx.Identity, prx.Facet, prx.Protocol);
                await prx.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ConnectionRefusedException)
            {
            }
            registry.AddObject(IObjectPrx.Parse(
                ice1 ? "test3@TestAdapter" : "ice:TestAdapter//test3", communicator));
            try
            {
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            registry.AddObject(IObjectPrx.Parse(ice1 ? "test4" : "ice:test4", communicator));
            try
            {
                await IObjectPrx.Parse(ice1 ? "test4" : "ice:test4", communicator).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            output.WriteLine("ok");

            output.Write("testing locator cache background updates... ");
            output.Flush();
            {
                await using Communicator ic = TestHelper.CreateCommunicator(communicator.GetProperties());
                ic.DefaultLocationResolver = new LocationResolver(locator, new() { Background = true });

                var zeroBResolver = new LocationResolver(locator,
                                                         new() { Background = true, Ttl = TimeSpan.Zero });
                var oneBResolver = new LocationResolver(locator,
                                                        new() { Background = true, Ttl = TimeSpan.FromSeconds(1) });

                RegisterAdapterEndpoints(
                    registry,
                    "TestAdapter5",
                    "",
                    ResolveLocation(locator, "TestAdapter")!);
                registry.AddObject(IObjectPrx.Parse(
                    ice1 ? "test3@TestAdapter" : "ice:TestAdapter//test3", communicator));

                count = locator.GetRequestCount();
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                    .Clone(locationResolver: zeroBResolver).IcePingAsync(); // No locator cache.
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", ic).Clone(locationResolver: zeroBResolver).IcePingAsync(); // No locator cache.
                count += 3;
                TestHelper.Assert(count == locator.GetRequestCount());

                await IObjectPrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                    .Clone(locationResolver: oneBResolver).IcePingAsync(); // 1s timeout.
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                    .Clone(locationResolver: oneBResolver).IcePingAsync(); // 1s timeout.

                registry.AddObject(IObjectPrx.Parse(helper.GetTestProxy("test3", 99), communicator));
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                    .Clone(locationResolver: oneBResolver).IcePingAsync();

                count += 3;
                TestHelper.Assert(count == locator.GetRequestCount());

                UnregisterAdapterEndpoints(registry, "TestAdapter5", "");
                Thread.Sleep(1000);

                // The following request should trigger the background
                // updates but still use the cached endpoints and
                // therefore succeed.
                await IObjectPrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                    .Clone(locationResolver: oneBResolver).IcePingAsync(); // 1s timeout.
                await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                    .Clone(locationResolver: oneBResolver).IcePingAsync(); // 1s timeout.

                try
                {
                    while (true)
                    {
                        await IObjectPrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                            .Clone(locationResolver: oneResolver).IcePingAsync(); // 1s timeout.
                        Thread.Sleep(10);
                    }
                }
                catch
                {
                    // Expected to fail once they endpoints have been updated in the background.
                }
                try
                {
                    while (true)
                    {
                        await IObjectPrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                            .Clone(locationResolver: oneResolver).IcePingAsync(); // 1s timeout.
                        Thread.Sleep(10);
                    }
                }
                catch
                {
                    // Expected to fail once they endpoints have been updated in the background.
                }
            }
            output.WriteLine("ok");

            output.Write("testing proxy from server after shutdown... ");
            output.Flush();
            hello = obj1.GetReplicatedHello();
            TestHelper.Assert(hello != null);
            obj1.Shutdown();
            manager.StartServer();
            hello.SayHello();
            output.WriteLine("ok");

            // TODO: this does not work with ice2 because we currently don't retry on any remote exception, including
            // ONE.
            if (ice1)
            {
                output.Write("testing object migration... ");
                output.Flush();
                hello = IHelloPrx.Parse(ice1 ? "hello" : "ice:hello", communicator);
                obj1.MigrateHello();
                _ = (await hello.GetConnectionAsync()).GoAwayAsync();
                hello.SayHello();
                obj1.MigrateHello();
                hello.SayHello();
                obj1.MigrateHello();
                hello.SayHello();
                output.WriteLine("ok");
            }

            output.Write("shutdown server... ");
            output.Flush();
            obj1.Shutdown();
            output.WriteLine("ok");

            output.Write("testing whether server is gone... ");
            output.Flush();
            try
            {
                await obj2.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            try
            {
                await obj3.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            try
            {
                TestHelper.Assert(obj5 != null);
                await obj5.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            output.WriteLine("ok");

            output.Write("testing indirect proxies to colocated objects... ");
            output.Flush();

            await using var adapter = new ObjectAdapter(
                communicator,
                new() { Endpoints = helper.GetTestEndpoint(ephemeral: true) });

            var id = new Identity(Guid.NewGuid().ToString(), "");
            adapter.Add(id, new Hello());
            await adapter.ActivateAsync();

            // Ensure that calls on the well-known proxy is collocated.
            IHelloPrx? helloPrx;
            if (ice1)
            {
                helloPrx = IHelloPrx.Parse($"{id}", communicator);
            }
            else
            {
                helloPrx = IHelloPrx.Parse($"ice:{id}", communicator);
            }
            TestHelper.Assert(await helloPrx.GetConnectionAsync() is ColocatedConnection);

            // Ensure that calls on the indirect proxy (with adapter ID) is colocated
            helloPrx = await adapter.CreateProxy(id, IObjectPrx.Factory).CheckedCastAsync(IHelloPrx.Factory);
            TestHelper.Assert(helloPrx != null && await helloPrx.GetConnectionAsync() is ColocatedConnection);

            // Ensure that calls on the direct proxy is colocated
            helloPrx = await adapter.CreateProxy(id, IObjectPrx.Factory).Clone(
                endpoints: adapter.PublishedEndpoints,
                location: ImmutableArray<string>.Empty).CheckedCastAsync(IHelloPrx.Factory);
            TestHelper.Assert(helloPrx != null && await helloPrx.GetConnectionAsync() is ColocatedConnection);

            output.WriteLine("ok");

            output.Write("shutdown server manager... ");
            output.Flush();
            manager.Shutdown();
            output.WriteLine("ok");
        }

        private static void RegisterAdapterEndpoints(
            ILocatorRegistryPrx registry,
            string adapterId,
            string replicaGroupId,
            IObjectPrx proxy)
        {
            if (proxy.Protocol == Protocol.Ice1)
            {
                registry.SetReplicatedAdapterDirectProxy(adapterId, replicaGroupId, proxy);
            }
            else
            {
                registry.RegisterAdapterEndpoints(adapterId, replicaGroupId, proxy.Endpoints.ToEndpointDataList());
            }
        }

        private static IObjectPrx? ResolveLocation(ILocatorPrx locator, string adapterId)
        {
            if (locator.Protocol == Protocol.Ice1)
            {
                return locator.FindAdapterById(adapterId);
            }
            else
            {
                EndpointData[] dataArray = locator.ResolveLocation(ImmutableArray.Create(adapterId));

                return dataArray.Length > 0 ?
                    locator.Clone(endpoints: dataArray.ToEndpointList(locator.Communicator)) : null;
            }
        }

        private static void UnregisterAdapterEndpoints(
            ILocatorRegistryPrx registry,
            string adapterId,
            string replicaGroupId)
        {
            if (registry.Protocol == Protocol.Ice1)
            {
                registry.SetReplicatedAdapterDirectProxy(adapterId, replicaGroupId, null);
            }
            else
            {
                registry.UnregisterAdapterEndpoints(adapterId, replicaGroupId);
            }
        }
    }
}
