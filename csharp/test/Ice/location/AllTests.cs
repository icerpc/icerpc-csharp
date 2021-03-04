// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop.ZeroC.Ice;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Location
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;
            TextWriter output = helper.Output;

            bool ice1 = helper.Protocol == Protocol.Ice1;
            var manager = IServerManagerPrx.Parse(helper.GetTestProxy("ServerManager", 0), communicator);
            var locator = ITestLocatorPrx.Parse(helper.GetTestProxy("locator", 0), communicator);

            ILocationService locationService = new LocationService(locator);
            communicator.DefaultLocationService = locationService;

            var registry = ITestLocatorRegistryPrx.Factory.Clone(locator.GetRegistry()!);
            TestHelper.Assert(registry != null);

            output.Write("testing ice1 string/URI parsing... ");
            output.Flush();
            IServicePrx base1, base2, base3, base4, base5, base6, base7;
            if (ice1)
            {
                base1 = IServicePrx.Parse("test @ TestAdapter", communicator);
                base2 = IServicePrx.Parse("test @ TestAdapter", communicator);
                base3 = IServicePrx.Parse(ice1 ? "test" : "ice:test", communicator);
                base4 = IServicePrx.Parse("ServerManager", communicator);
                base5 = IServicePrx.Parse("test2", communicator);
                base6 = IServicePrx.Parse("test @ ReplicatedAdapter", communicator);
                base7 = IServicePrx.Parse("test3 -f facet", communicator);
            }
            else
            {
                base1 = IServicePrx.Parse("ice:TestAdapter//test", communicator);
                base2 = IServicePrx.Parse("ice:TestAdapter//test", communicator);
                base3 = IServicePrx.Parse("ice:test", communicator);
                base4 = IServicePrx.Parse("ice:ServerManager", communicator);
                base5 = IServicePrx.Parse("ice:test2", communicator);
                base6 = IServicePrx.Parse("ice:ReplicatedAdapter//test", communicator);
                base7 = IServicePrx.Parse("ice:test3#facet", communicator);
            }
            output.WriteLine("ok");

            output.Write("testing LocationService... ");
            TestHelper.Assert(base1.LocationService == communicator.DefaultLocationService);
            var anotherLocationService =
                new LocationService(ILocatorPrx.Parse(ice1 ? "anotherLocator" : "ice:anotherLocator", communicator));
            base1 = base1.Clone(locationService: anotherLocationService);
            TestHelper.Assert(base1.LocationService == anotherLocationService);
            communicator.DefaultLocationService = null;
            base1 = IServicePrx.Parse(ice1 ? "test @ TestAdapter" : "ice:TestAdapter//test", communicator);
            TestHelper.Assert(base1.LocationService == null);
            base1 = base1.Clone(locationService: anotherLocationService);
            TestHelper.Assert(base1.LocationService == anotherLocationService);
            communicator.DefaultLocationService = locationService;
            base1 = IServicePrx.Parse(ice1 ? "test @ TestAdapter" : "ice:TestAdapter//test", communicator);
            TestHelper.Assert(base1.LocationService == communicator.DefaultLocationService);
            output.WriteLine("ok");

            output.Write("starting server... ");
            output.Flush();
            manager.StartServer();
            output.WriteLine("ok");

            output.Write("testing checked cast... ");
            output.Flush();
            var obj1 = await ITestIntfPrx.Factory.CheckedCastAsync(base1);
            TestHelper.Assert(obj1 != null);
            var obj2 = await ITestIntfPrx.Factory.CheckedCastAsync(base2);
            TestHelper.Assert(obj2 != null);
            var obj3 = await ITestIntfPrx.Factory.CheckedCastAsync(base3);
            TestHelper.Assert(obj3 != null);
            var obj4 = await IServerManagerPrx.Factory.CheckedCastAsync(base4);
            TestHelper.Assert(obj4 != null);
            var obj5 = await ITestIntfPrx.Factory.CheckedCastAsync(base5);
            TestHelper.Assert(obj5 != null);
            var obj6 = await ITestIntfPrx.Factory.CheckedCastAsync(base6);
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
                obj5 = await ITestIntfPrx.Factory.CheckedCastAsync(base5);
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
                base1 = IServicePrx.Parse(ice1 ? "unknown/unknown" : "ice:unknown/unknown", communicator);
                await base1.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            output.WriteLine("ok");

            output.Write("testing proxy with unknown server... ");
            output.Flush();
            try
            {
                base1 = IServicePrx.Parse(
                    ice1 ? "test @ TestAdapterUnknown" : "ice:TestAdapterUnknown//test", communicator);
                await base1.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            output.WriteLine("ok");

            output.Write("testing location service TTL... ");
            output.Flush();

            var zeroLocationService = new LocationService(locator, new() { Ttl = TimeSpan.Zero });

            IServicePrx basencc = IServicePrx.Parse(
                ice1 ? "test@TestAdapter" : "ice:TestAdapter//test", communicator).Clone(
                    cacheConnection: false,
                    locationService: zeroLocationService);
            int count = locator.GetRequestCount();
            await basencc.IcePingAsync(); // No locator cache.
            TestHelper.Assert(++count == locator.GetRequestCount());
            await basencc.IcePingAsync(); // No locator cache.
            TestHelper.Assert(++count == locator.GetRequestCount());

            var twoLocationService = new LocationService(locator, new() { Ttl = TimeSpan.FromSeconds(2) });
            basencc = basencc.Clone(locationService: twoLocationService);
            await basencc.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());
            await basencc.IcePingAsync();
            TestHelper.Assert(count == locator.GetRequestCount());

            var oneLocationService = new LocationService(locator, new() { Ttl = TimeSpan.FromSeconds(1) });
            basencc = basencc.Clone(locationService: oneLocationService);
            await basencc.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());
            Thread.Sleep(1300); // 1300ms > 1s
            await basencc.IcePingAsync();
            TestHelper.Assert(++count == locator.GetRequestCount());

            basencc = basencc.Clone(locationService: communicator.DefaultLocationService); // infinite timeout
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
            TestHelper.Assert(hello.Location == "TestAdapter");
            hello.SayHello();
            hello = obj1.GetReplicatedHello();
            TestHelper.Assert(hello != null);
            TestHelper.Assert(hello.Location == "ReplicatedAdapter");
            hello.SayHello();
            output.WriteLine("ok");

            output.Write("testing locator request queuing... ");
            output.Flush();
            hello = obj1.GetReplicatedHello()!.Clone(locationService: zeroLocationService, cacheConnection: false);

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
            hello = hello.Clone(location: "unknown");
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

            output.Write("testing server locator cache... ");
            output.Flush();
            try
            {
                await IServicePrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
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
                await IServicePrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();

                RegisterAdapterEndpoints(
                    registry,
                    "TestAdapter3",
                    replicaGroupId: "",
                    IServicePrx.Parse(helper.GetTestProxy("dummy", 99), communicator));

                await IServicePrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            try
            {
                await IServicePrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).Clone(
                    locationService: zeroLocationService).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ConnectionRefusedException)
            {
            }

            try
            {
                await IServicePrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
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
                await IServicePrx.Parse(ice1 ? "test@TestAdapter3" : "ice:TestAdapter3//test", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }
            output.WriteLine("ok");

            output.Write("testing well-known object locator cache... ");
            output.Flush();
            registry.AddObject(IServicePrx.Parse(
                ice1 ? "test3@TestUnknown" : "ice:TestUnknown//test3", communicator));
            try
            {
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            registry.AddObject(IServicePrx.Parse(
                ice1 ? "test3@TestAdapter4" : "ice:TestAdapter4//test3", communicator)); // Update
            RegisterAdapterEndpoints(
                registry,
                "TestAdapter4",
                "",
                IServicePrx.Parse(helper.GetTestProxy("dummy", 99), communicator));
            try
            {
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
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
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            RegisterAdapterEndpoints(
                registry,
                "TestAdapter4",
                "",
                IServicePrx.Parse(helper.GetTestProxy("dummy", 99), communicator));
            try
            {
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            try
            {
                await IServicePrx.Parse(ice1 ? "test@TestAdapter4" : "ice:TestAdapter4//test", communicator).Clone(
                    locationService: zeroLocationService).IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ConnectionRefusedException)
            {
            }

            registry.AddObject(IServicePrx.Parse(
                ice1 ? "test3@TestAdapter" : "ice:TestAdapter//test3", communicator));
            try
            {
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", communicator).IcePingAsync();
            }
            catch
            {
                TestHelper.Assert(false);
            }

            registry.AddObject(IServicePrx.Parse(ice1 ? "test4" : "ice:test4", communicator));
            try
            {
                await IServicePrx.Parse(ice1 ? "test4" : "ice:test4", communicator).IcePingAsync();
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
                ic.DefaultLocationService = new LocationService(locator, new() { Background = true });

                var zeroBLocationService = new LocationService(locator,
                                                         new() { Background = true, Ttl = TimeSpan.Zero });
                var oneBLocationService = new LocationService(locator,
                                                        new() { Background = true, Ttl = TimeSpan.FromSeconds(1) });

                RegisterAdapterEndpoints(
                    registry,
                    "TestAdapter5",
                    "",
                    ResolveLocation(locator, "TestAdapter")!);
                registry.AddObject(IServicePrx.Parse(
                    ice1 ? "test3@TestAdapter" : "ice:TestAdapter//test3", communicator));

                count = locator.GetRequestCount();
                await IServicePrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                    .Clone(locationService: zeroBLocationService).IcePingAsync(); // No locator cache.
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", ic).Clone(locationService: zeroBLocationService).IcePingAsync(); // No locator cache.
                count += 3;
                TestHelper.Assert(count == locator.GetRequestCount());

                await IServicePrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                    .Clone(locationService: oneBLocationService).IcePingAsync(); // 1s timeout.
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                    .Clone(locationService: oneBLocationService).IcePingAsync(); // 1s timeout.

                registry.AddObject(IServicePrx.Parse(helper.GetTestProxy("test3", 99), communicator));
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                    .Clone(locationService: oneBLocationService).IcePingAsync();

                count += 3;
                TestHelper.Assert(count == locator.GetRequestCount());

                UnregisterAdapterEndpoints(registry, "TestAdapter5", "");
                Thread.Sleep(1000);

                // The following request should trigger the background
                // updates but still use the cached endpoints and
                // therefore succeed.
                await IServicePrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                    .Clone(locationService: oneBLocationService).IcePingAsync(); // 1s timeout.
                await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                    .Clone(locationService: oneBLocationService).IcePingAsync(); // 1s timeout.

                try
                {
                    while (true)
                    {
                        await IServicePrx.Parse(ice1 ? "test@TestAdapter5" : "ice:TestAdapter5//test", ic)
                            .Clone(locationService: oneLocationService).IcePingAsync(); // 1s timeout.
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
                        await IServicePrx.Parse(ice1 ? "test3" : "ice:test3", ic)
                            .Clone(locationService: oneLocationService).IcePingAsync(); // 1s timeout.
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

            await using var server = new Server(
                communicator,
                new() { Endpoints = helper.GetTestEndpoint(ephemeral: true) });

            var id = Guid.NewGuid().ToString();
            server.Add(id, new Hello());
            await server.ActivateAsync();

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

            // Ensure that calls on the indirect proxy (with server ID) is colocated
            helloPrx = await IHelloPrx.Factory.CheckedCastAsync(IServicePrx.Factory.Create(server, id));
            TestHelper.Assert(helloPrx != null && await helloPrx.GetConnectionAsync() is ColocatedConnection);

            // Ensure that calls on the direct proxy is colocated
            helloPrx = await IHelloPrx.Factory.CheckedCastAsync(IServicePrx.Factory.Create(server, id).Clone(
                endpoints: server.PublishedEndpoints,
                location: ""));
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
            IServicePrx proxy)
        {
            registry.SetReplicatedAdapterDirectProxy(adapterId, replicaGroupId, proxy);
        }

        private static IServicePrx? ResolveLocation(ILocatorPrx locator, string adapterId) =>
            locator.FindAdapterById(adapterId);

        private static void UnregisterAdapterEndpoints(
            ILocatorRegistryPrx registry,
            string adapterId,
            string replicaGroupId)
        {
            registry.SetReplicatedAdapterDirectProxy(adapterId, replicaGroupId, null);
        }
    }
}
