// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Proxy
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;

            bool ice1 = helper.Protocol == Protocol.Ice1;
            System.IO.TextWriter output = helper.Output;
            output.Write("testing proxy parsing... ");
            output.Flush();

            string rf = helper.GetTestProxy("test");
            var baseProxy = IServicePrx.Parse(rf, communicator);
            TestHelper.Assert(baseProxy != null);

            var b1 = IServicePrx.Parse("ice:test", communicator);
            TestHelper.Assert(!b1.IsOneway);

            b1 = IServicePrx.Parse("test", communicator);
            TestHelper.Assert(!b1.IsOneway);

            b1 = IServicePrx.Parse("test -t", communicator);
            TestHelper.Assert(!b1.IsOneway);

            b1 = IServicePrx.Parse("test -o", communicator);
            TestHelper.Assert(b1.IsOneway);

            b1 = IServicePrx.Parse("test -O", communicator);
            TestHelper.Assert(b1.IsOneway);

            b1 = IServicePrx.Parse("test -d", communicator);
            TestHelper.Assert(b1.IsOneway);

            b1 = IServicePrx.Parse("test -D", communicator);
            TestHelper.Assert(b1.IsOneway);

            b1 = IServicePrx.Parse("ice:test", communicator);
            TestHelper.Assert(b1.Protocol == Protocol.Ice2 && b1.Encoding == Encoding.V20);
            b1 = IServicePrx.Parse("test", communicator);
            TestHelper.Assert(b1.Protocol == Protocol.Ice1 && b1.Encoding == Encoding.V11);

            b1 = IServicePrx.Parse("ice:test?encoding=6.5", communicator);
            TestHelper.Assert(b1.Encoding.Major == 6 && b1.Encoding.Minor == 5);
            b1 = IServicePrx.Parse("test -e 6.5", communicator);
            TestHelper.Assert(b1.Encoding.Major == 6 && b1.Encoding.Minor == 5);

            b1 = IServicePrx.Parse("ice:test?encoding=2.1&protocol=6", communicator);
            TestHelper.Assert(b1.Protocol == (Protocol)6 && b1.Encoding.Major == 2 && b1.Encoding.Minor == 1);

            // Test invalid endpoint syntax
            // TODO: why are we testing this here?
            try
            {
                await using var badOa = new Server(communicator, new() { Endpoints = " : " });
                TestHelper.Assert(false);
            }
            catch (FormatException)
            {
            }

            try
            {
                await using var badOa = new Server(communicator, new() { Endpoints = "tcp: "});
                TestHelper.Assert(false);
            }
            catch (FormatException)
            {
            }

            try
            {
                await using var badOa = new Server(communicator, new() { Endpoints = ":tcp" });
                TestHelper.Assert(false);
            }
            catch (FormatException)
            {
            }

            output.WriteLine("ok");

            output.Write("testing fixed proxies... ");
            output.Flush();
            b1 = IServicePrx.Parse(rf, communicator);

            Connection connection = await b1.GetConnectionAsync();
            IServicePrx b2 = IServicePrx.Factory.Create(connection, "fixed");
            if (connection.Protocol == Protocol.Ice1)
            {
                TestHelper.Assert(b2.ToString() == "fixed -t -e 1.1");
            }
            else
            {
                TestHelper.Assert(b2.ToString() == "ice:/fixed?fixed=true");
            }
            output.WriteLine("ok");

            output.Write("testing proxy options and properties... ");
            output.Flush();

            string propertyPrefix = "Foo.Proxy";
            string proxyString = helper.GetTestProxy("test", 0);

            // For ice1, we parse the property tree while for ice2 we usually just parse the string-value since it's
            // equivalent.

            communicator.SetProperty(propertyPrefix, proxyString);
            b1 = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            TestHelper.Assert(b1.Path == "/test");

            TestHelper.Assert(b1.CacheConnection);
            if (ice1)
            {
                string property = propertyPrefix + ".CacheConnection";
                communicator.SetProperty(property, "0");
                b1 = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
                communicator.RemoveProperty(property);
            }
            else
            {
                b1 = IServicePrx.Parse($"{proxyString}?cache-connection=false", communicator);
            }
            TestHelper.Assert(!b1.CacheConnection);

            if (ice1)
            {
                string property = propertyPrefix + ".Context.c1";
                TestHelper.Assert(!b1.Context.ContainsKey("c1"));
                communicator.SetProperty(property, "TEST1");
                b1 = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
                TestHelper.Assert(b1.Context["c1"] == "TEST1");

                property = propertyPrefix + ".Context.c2";
                TestHelper.Assert(!b1.Context.ContainsKey("c2"));
                communicator.SetProperty(property, "TEST2");
                b1 = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
                TestHelper.Assert(b1.Context["c2"] == "TEST2");

                communicator.SetProperty(propertyPrefix + ".Context.c1", "");
                communicator.SetProperty(propertyPrefix + ".Context.c2", "");
            }
            else
            {
                b1 = IServicePrx.Parse(
                    $"{proxyString}?context=c1=TEST1,c2=TEST&context=c2=TEST2,d%204=TEST%204,c3=TEST3",
                    communicator);

                TestHelper.Assert(b1.Context.Count == 4);
                TestHelper.Assert(b1.Context["c1"] == "TEST1");
                TestHelper.Assert(b1.Context["c2"] == "TEST2");
                TestHelper.Assert(b1.Context["c3"] == "TEST3");
                TestHelper.Assert(b1.Context["d 4"] == "TEST 4");

                // This works because Context is a sorted dictionary
                TestHelper.Assert(b1.ToString() ==
                    $"{proxyString}?context=c1=TEST1,c2=TEST2,c3=TEST3,d%204=TEST%204");
            }

            TestHelper.Assert(b1.InvocationTimeout == TimeSpan.FromSeconds(60));
            if (ice1)
            {
                string property = propertyPrefix + ".InvocationTimeout";
                communicator.SetProperty(property, "1s");
                b1 = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
                communicator.SetProperty(property, "");
            }
            else
            {
                b1 = IServicePrx.Parse($"{proxyString}?invocation-timeout=1s", communicator);
            }
            TestHelper.Assert(b1.InvocationTimeout == TimeSpan.FromSeconds(1));

            TestHelper.Assert(b1.PreferNonSecure == communicator.DefaultPreferNonSecure);
            if (ice1)
            {
                string property = propertyPrefix + ".PreferNonSecure";
                communicator.SetProperty(property, "SameHost");
                b1 = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
                communicator.RemoveProperty(property);
            }
            else
            {
                b1 = IServicePrx.Parse($"{proxyString}?prefer-non-secure=SameHost",
                                      communicator);
            }
            TestHelper.Assert(b1.PreferNonSecure != communicator.DefaultPreferNonSecure);

            if (!ice1)
            {
                string complicated = $"{proxyString}?invocation-timeout=10s&context=c%201=some%20value" +
                    "&alt-endpoint=ice+ws://localhost?resource=/x/y$source-address=[::1]&context=c5=v5";
                b1 = IServicePrx.Parse(complicated, communicator);

                TestHelper.Assert(b1.Endpoints.Count == 2);
                TestHelper.Assert(b1.Endpoints[1].Transport == Transport.WS);
                TestHelper.Assert(b1.Endpoints[1]["resource"] == "/x/y");
                TestHelper.Assert(b1.Endpoints[1]["source-address"] == "::1");
                TestHelper.Assert(b1.Context.Count == 2);
                TestHelper.Assert(b1.Context["c 1"] == "some value");
                TestHelper.Assert(b1.Context["c5"] == "v5");
            }

            output.WriteLine("ok");

            output.Write("testing IServicePrx.ToProperty... ");
            output.Flush();

            b1 = IServicePrx.Parse(
                ice1 ? "test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 1000" : "ice+tcp://127.0.0.1/test",
                communicator).Clone(cacheConnection: true,
                                    preferExistingConnection: true,
                                    preferNonSecure: NonSecure.Never,
                                    invocationTimeout: TimeSpan.FromSeconds(10));

            Dictionary<string, string> proxyProps = b1.ToProperty("Test");
            // InvocationTimeout is a property with ice1 and an URI option with ice2 so the extra property with ice1.
            TestHelper.Assert(proxyProps.Count == (ice1 ? 4 : 1));

            TestHelper.Assert(proxyProps["Test"] ==
                (ice1 ? "test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 1000" :
                        "ice+tcp://127.0.0.1/test?invocation-timeout=10s&prefer-existing-connection=true&prefer-non-secure=never"));

            if (ice1)
            {
                TestHelper.Assert(proxyProps["Test.InvocationTimeout"] == "10s");
                TestHelper.Assert(proxyProps["Test.PreferNonSecure"] == "Never");

                ILocatorPrx locator = ILocatorPrx.Parse("locator", communicator).Clone(
                    cacheConnection: false,
                    preferExistingConnection: false,
                    preferNonSecure: NonSecure.Always);

                // TODO: LocationService should reject indirect locators.
                ILocationService locationService = new LocationService(locator);
                b1 = b1.Clone(locationService: locationService);

                proxyProps = b1.ToProperty("Test");

                TestHelper.Assert(proxyProps.Count == 4, $"count: {proxyProps.Count}");
                TestHelper.Assert(proxyProps["Test"] == "test -t -e 1.1");
                TestHelper.Assert(proxyProps["Test.InvocationTimeout"] == "10s");
                TestHelper.Assert(proxyProps["Test.PreferNonSecure"] == "Never");
                TestHelper.Assert(proxyProps["Test.PreferExistingConnection"] == "true");
            }

            output.WriteLine("ok");

            output.Write("testing IServicePrx.Communicator... ");
            output.Flush();
            TestHelper.Assert(baseProxy.Communicator == communicator);
            output.WriteLine("ok");

            output.Write("testing proxy Clone... ");

            if (ice1)
            {
                TestHelper.Assert(IServicePrx.Factory.Clone(baseProxy, facet: "facet").Facet == "facet");
                TestHelper.Assert(baseProxy.Clone(location: "id").Location == "id");
            }

            TestHelper.Assert(!baseProxy.Clone(oneway: false).IsOneway);
            TestHelper.Assert(baseProxy.Clone(oneway: true).IsOneway);

            if (ice1)
            {
                IServicePrx other = IServicePrx.Factory.Clone(baseProxy, path: "test", facet: "facet");
                TestHelper.Assert(other.Facet == "facet");
                TestHelper.Assert(other.Identity.Name == "test");
                TestHelper.Assert(other.Identity.Category.Length == 0);

                other = IServicePrx.Factory.Clone(other, path: "category/test");
                TestHelper.Assert(other.Facet.Length == 0);
                TestHelper.Assert(other.Identity.Name == "test");
                TestHelper.Assert(other.Identity.Category == "category");

                other = IServicePrx.Factory.Clone(baseProxy, path: "foo", facet: "facet1");
                TestHelper.Assert(other.Facet == "facet1");
                TestHelper.Assert(other.Identity.Name == "foo");
                TestHelper.Assert(other.Identity.Category.Length == 0);
            }

            TestHelper.Assert(baseProxy.Clone(preferNonSecure: NonSecure.Always).PreferNonSecure == NonSecure.Always);
            TestHelper.Assert(baseProxy.Clone(preferNonSecure: NonSecure.Never).PreferNonSecure == NonSecure.Never);

            output.WriteLine("ok");

            output.Write("testing proxy comparison... ");
            output.Flush();

            TestHelper.Assert(IServicePrx.Parse("ice:foo", communicator).Equals(IServicePrx.Parse("ice:foo", communicator)));
            TestHelper.Assert(!IServicePrx.Parse("ice:foo", communicator).Equals(IServicePrx.Parse("ice:foo2", communicator)));

            var compObj = IServicePrx.Parse(ice1 ? "foo" : "ice:foo", communicator);

            if (ice1)
            {
                TestHelper.Assert(IServicePrx.Factory.Clone(compObj, facet: "facet").Equals(
                                IServicePrx.Factory.Clone(compObj, facet: "facet")));
                TestHelper.Assert(!IServicePrx.Factory.Clone(compObj, facet: "facet").Equals(
                                IServicePrx.Factory.Clone(compObj, facet: "facet1")));
            }

            TestHelper.Assert(compObj.Clone(oneway: true).Equals(compObj.Clone(oneway: true)));
            TestHelper.Assert(!compObj.Clone(oneway: true).Equals(compObj.Clone(oneway: false)));

            TestHelper.Assert(compObj.Clone(cacheConnection: true).Equals(compObj.Clone(cacheConnection: true)));
            TestHelper.Assert(!compObj.Clone(cacheConnection: false).Equals(compObj.Clone(cacheConnection: true)));

            TestHelper.Assert(compObj.Clone(label: "id2").Equals(compObj.Clone(label: "id2")));
            TestHelper.Assert(!compObj.Clone(label: "id1").Equals(compObj.Clone(label: "id2")));
            TestHelper.Assert(Equals(compObj.Clone(label: "id1").Label, "id1"));
            TestHelper.Assert(Equals(compObj.Clone(label: "id2").Label, "id2"));

            if (ice1)
            {
                var loc1 = new LocationService(ILocatorPrx.Parse("ice+tcp://host:10000/loc1", communicator));
                var loc2 = new LocationService(ILocatorPrx.Parse("ice+tcp://host:10000/loc2", communicator));
                TestHelper.Assert(compObj.Clone(clearLocationService: true).Equals(compObj.Clone(clearLocationService: true)));
                TestHelper.Assert(compObj.Clone(locationService: loc1).Equals(compObj.Clone(locationService: loc1)));
                TestHelper.Assert(!compObj.Clone(locationService: loc1).Equals(compObj.Clone(clearLocationService: true)));
                TestHelper.Assert(!compObj.Clone(clearLocationService: true).Equals(compObj.Clone(locationService: loc2)));
                TestHelper.Assert(!compObj.Clone(locationService: loc1).Equals(compObj.Clone(locationService: loc2)));
            }

            var ctx1 = new Dictionary<string, string>
            {
                ["ctx1"] = "v1"
            };
            var ctx2 = new Dictionary<string, string>
            {
                ["ctx2"] = "v2"
            };
            TestHelper.Assert(compObj.Clone(context: new Dictionary<string, string>()).Equals(
                              compObj.Clone(context: new Dictionary<string, string>())));
            TestHelper.Assert(compObj.Clone(context: ctx1).Equals(compObj.Clone(context: ctx1)));
            TestHelper.Assert(!compObj.Clone(context: ctx1).Equals(
                              compObj.Clone(context: new Dictionary<string, string>())));
            TestHelper.Assert(!compObj.Clone(context: new Dictionary<string, string>()).Equals(
                              compObj.Clone(context: ctx2)));
            TestHelper.Assert(!compObj.Clone(context: ctx1).Equals(compObj.Clone(context: ctx2)));

            TestHelper.Assert(compObj.Clone(preferNonSecure: NonSecure.Always).Equals(
                              compObj.Clone(preferNonSecure: NonSecure.Always)));
            TestHelper.Assert(!compObj.Clone(preferNonSecure: NonSecure.Always).Equals(
                              compObj.Clone(preferNonSecure: NonSecure.Never)));

            var compObj1 = IServicePrx.Parse("ice+tcp://127.0.0.1:10000/foo", communicator);
            var compObj2 = IServicePrx.Parse("ice+tcp://127.0.0.1:10001/foo", communicator);
            TestHelper.Assert(!compObj1.Equals(compObj2));

            compObj1 = IServicePrx.Parse("ice:MyAdapter1//foo", communicator);
            compObj2 = IServicePrx.Parse("ice:MyAdapter2//foo", communicator);
            TestHelper.Assert(!compObj1.Equals(compObj2));

            if (ice1)
            {
                compObj1 = IServicePrx.Parse("foo@MyAdapter1", communicator);
                var loc = new LocationService(ILocatorPrx.Parse("ice+tcp://host:10000/loc", communicator));
                TestHelper.Assert(compObj1.Clone(locationService: loc).Equals(
                    compObj1.Clone(locationService: loc)));
            }

            compObj1 = IServicePrx.Parse("ice+tcp://127.0.0.1:10000/foo", communicator);
            compObj2 = IServicePrx.Parse("ice:MyAdapter1//foo", communicator);
            TestHelper.Assert(!compObj1.Equals(compObj2));

            IReadOnlyList<Endpoint> endpts1 =
                IServicePrx.Parse("ice+tcp://127.0.0.1:10000/foo", communicator).Endpoints;
            IReadOnlyList<Endpoint> endpts2 =
                IServicePrx.Parse("ice+tcp://127.0.0.1:10001/foo", communicator).Endpoints;
            TestHelper.Assert(!endpts1[0].Equals(endpts2[0]));
            TestHelper.Assert(endpts1[0].Equals(
                IServicePrx.Parse("ice+tcp://127.0.0.1:10000/foo", communicator).Endpoints[0]));

            if (await baseProxy.GetConnectionAsync() is IPConnection baseConnection)
            {
                Connection baseConnection2 = await baseProxy.Clone(label: "base2").GetConnectionAsync();
                compObj1 = compObj1.Clone(fixedConnection: baseConnection);
                compObj2 = compObj2.Clone(fixedConnection: baseConnection2);
                TestHelper.Assert(!compObj1.Equals(compObj2));
            }

            output.WriteLine("ok");

            output.Write("testing checked cast... ");
            output.Flush();
            var cl = await IMyClassPrx.Factory.CheckedCastAsync(baseProxy);
            TestHelper.Assert(cl != null);
            var derived = await IMyDerivedClassPrx.Factory.CheckedCastAsync(cl);
            TestHelper.Assert(derived != null);
            TestHelper.Assert(cl.Equals(baseProxy));
            TestHelper.Assert(derived.Equals(baseProxy));
            TestHelper.Assert(cl.Equals(derived));

            if (ice1)
            {
                try
                {
                    await IMyDerivedClassPrx.Factory.Clone(cl, facet: "facet").IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (ServiceNotFoundException)
                {
                }
            }
            output.WriteLine("ok");

            output.Write("testing checked cast with context... ");
            output.Flush();

            SortedDictionary<string, string> c = cl.GetContext();
            TestHelper.Assert(c == null || c.Count == 0);

            c = new SortedDictionary<string, string>
            {
                ["one"] = "hello",
                ["two"] = "world"
            };
            cl = await IMyClassPrx.Factory.CheckedCastAsync(baseProxy, c);
            SortedDictionary<string, string> c2 = cl!.GetContext();
            TestHelper.Assert(c.DictionaryEqual(c2));
            output.WriteLine("ok");

            if (ice1)
            {
                output.Write("testing ice2 proxy in 1.1 encapsulation... ");
                output.Flush();
                var ice2Prx = IServicePrx.Parse(
                    "ice+tcp://localhost:10000/foo?alt-endpoint=ice+ws://localhost:10000", communicator);
                var prx = IMyDerivedClassPrx.Factory.Clone(baseProxy).Echo(ice2Prx);
                TestHelper.Assert(ice2Prx.Equals(prx));

                // With a location dropped by the 1.1 encoding:
                ice2Prx = IServicePrx.Parse("ice+tcp://localhost:10000/location/foo", communicator);
                prx = IMyDerivedClassPrx.Factory.Clone(baseProxy).Echo(ice2Prx);
                TestHelper.Assert(ice2Prx.Clone(location: "").Equals(prx));
                output.WriteLine("ok");
            }
            else
            {
                output.Write("testing ice1 proxy in 2.0 encapsulation... ");
                output.Flush();
                var ice1Prx = IServicePrx.Parse(
                    "foo:tcp -h localhost -p 10000:udp -h localhost -p 10000", communicator);
                var prx = IMyDerivedClassPrx.Factory.Clone(baseProxy).Echo(ice1Prx);
                TestHelper.Assert(ice1Prx.Equals(prx));
                output.WriteLine("ok");
            }

            if (!ice1)
            {
                output.Write("testing relative proxies... ");
                {
                    await using Server oa = new Server(communicator);
                    (await cl.GetConnectionAsync()).Server = oa;

                    // It's a non-fixed ice2 proxy with no endpoints, i.e. a relative proxy
                    ICallbackPrx callback = oa.AddWithUUID(
                        new Callback((relativeTest, current, cancel) =>
                                     {
                                         TestHelper.Assert(relativeTest.IsFixed);
                                         return relativeTest.DoIt(cancel: cancel);
                                    }),
                        ICallbackPrx.Factory);

                    await callback.IcePingAsync(); // colocated call

                    IRelativeTestPrx relativeTest = cl.OpRelative(callback);
                    TestHelper.Assert(relativeTest.Endpoints == cl.Endpoints); // reference equality
                    TestHelper.Assert(relativeTest.DoIt() == 2);
                }
                output.WriteLine("ok");
            }

            output.Write("testing ice_fixed... ");
            output.Flush();
            {
                if (await cl.GetConnectionAsync() is Connection connection2)
                {
                    TestHelper.Assert(!cl.IsFixed);
                    IMyClassPrx prx = cl.Clone(fixedConnection: connection2);
                    TestHelper.Assert(prx.IsFixed);
                    await prx.IcePingAsync();

                    if (ice1)
                    {
                        TestHelper.Assert(IServicePrx.Factory.Clone(
                            cl,
                            facet: "facet",
                            fixedConnection: connection2).Facet == "facet");
                    }
                    TestHelper.Assert(cl.Clone(oneway: true, fixedConnection: connection2).IsOneway);
                    var ctx = new Dictionary<string, string>
                    {
                        ["one"] = "hello",
                        ["two"] = "world"
                    };
                    TestHelper.Assert(cl.Clone(fixedConnection: connection2).Context.Count == 0);
                    TestHelper.Assert(cl.Clone(context: ctx, fixedConnection: connection2).Context.Count == 2);
                    TestHelper.Assert(await cl.Clone(fixedConnection: connection2).GetConnectionAsync() == connection2);
                    TestHelper.Assert(await cl.Clone(fixedConnection: connection2).Clone(fixedConnection: connection2).GetConnectionAsync() == connection2);
                    Connection? fixedConnection = await cl.Clone(label: "ice_fixed").GetConnectionAsync();
                    TestHelper.Assert(await cl.Clone(fixedConnection: connection2).Clone(fixedConnection: fixedConnection).GetConnectionAsync() == fixedConnection);
                }
            }
            output.WriteLine("ok");

            output.Write("testing encoding versioning... ");
            string ref13 = helper.GetTestProxy("test", 0);
            IMyClassPrx cl13 = IMyClassPrx.Parse(ref13, communicator).Clone(encoding: new Encoding(1, 3));
            try
            {
                await cl13.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (NotSupportedException)
            {
                // expected
            }
            output.WriteLine("ok");

            if (helper.Protocol == Protocol.Ice2)
            {
                output.Write("testing protocol versioning... ");
                output.Flush();
                string ref3 = helper.GetTestProxy("test", 0);
                ref3 += "?protocol=3";

                string transport = helper.Transport;
                ref3 = ref3.Replace($"ice+{transport}", "ice+universal");
                ref3 += $"&transport={transport}";
                var cl3 = IMyClassPrx.Parse(ref3, communicator);
                try
                {
                    await cl3.IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (NotSupportedException)
                {
                    // expected
                }
                output.WriteLine("ok");
            }

            output.Write("testing ice2 universal endpoints... ");
            output.Flush();

            var p1 = IServicePrx.Parse("ice+universal://127.0.0.1:4062/test?transport=tcp", communicator);
            TestHelper.Assert(p1.ToString() == "ice+tcp://127.0.0.1/test"); // uses default port

            p1 = IServicePrx.Parse(
                "ice+universal://127.0.0.1:4062/test?transport=tcp&alt-endpoint=host2:10000?transport=tcp",
                communicator);
            TestHelper.Assert(p1.ToString() == "ice+tcp://127.0.0.1/test?alt-endpoint=host2:10000");

            p1 = IServicePrx.Parse(
                "ice+universal://127.0.0.1:4062/test?transport=tcp&alt-endpoint=host2:10000?transport=99$option=a",
                communicator);

            TestHelper.Assert(p1.ToString() ==
                "ice+tcp://127.0.0.1/test?alt-endpoint=ice+universal://host2:10000?transport=99$option=a");

            output.WriteLine("ok");

            output.Write("testing ice1 opaque endpoints... ");
            output.Flush();

            // Legal TCP endpoint expressed as opaque endpoint
            p1 = IServicePrx.Parse("test:opaque -e 1.1 -t 1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                                      communicator);
            TestHelper.Assert(p1.ToString() == "test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 10000");

            // Two legal TCP endpoints expressed as opaque endpoints
            p1 = IServicePrx.Parse(@"test:opaque -e 1.1 -t 1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==:
                opaque -e 1.1 -t 1 -v CTEyNy4wLjAuMusuAAAQJwAAAA==", communicator);
            TestHelper.Assert(p1.ToString() ==
                "test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 10000:tcp -h 127.0.0.2 -p 12011 -t 10000");

            // Test that an SSL endpoint and an unknown-transport endpoint get written back out as an opaque
            // endpoint.
            p1 = IServicePrx.Parse(
                "test:opaque -e 1.1 -t 2 -v CTEyNy4wLjAuMREnAAD/////AA==:opaque -e 1.1 -t 99 -v abch",
                communicator);
            TestHelper.Assert(p1.ToString() ==
                "test -t -e 1.1:ssl -h 127.0.0.1 -p 10001 -t -1:opaque -t 99 -e 1.1 -v abch");

            output.WriteLine("ok");

            // TODO test communicator destroy in its own test
            output.Write("testing communicator shutdown... ");
            output.Flush();
            {
                var com = new Communicator();
                await com.DisposeAsync();
                await com.DisposeAsync();
            }
            output.WriteLine("ok");

            output.Write("testing communicator default source address... ");
            output.Flush();
            {
                await using var comm1 = new Communicator(new Dictionary<string, string>()
                    {
                        { "Ice.Default.SourceAddress", "192.168.1.40" }
                    });

                await using var comm2 = new Communicator();

                string[] proxyArray =
                    {
                        "ice+tcp://host.zeroc.com/identity#facet",
                        "ice -t:tcp -h localhost -p 10000",
                    };

                foreach (string s in proxyArray)
                {
                    var prx = IServicePrx.Parse(s, comm1);
                    TestHelper.Assert(prx.Endpoints[0]["source-address"] == "192.168.1.40");
                    prx = IServicePrx.Parse(s, comm2);
                    TestHelper.Assert(prx.Endpoints[0]["source-address"] == null);
                }
            }
            output.WriteLine("ok");

            output.Write("testing communicator default invocation timeout... ");
            output.Flush();
            {
                await using var comm1 = new Communicator(new Dictionary<string, string>()
                    {
                        { "Ice.Default.InvocationTimeout", "120s" }
                    });

                await using var comm2 = new Communicator();

                TestHelper.Assert(IServicePrx.Parse("ice+tcp://localhost/identity", comm1).InvocationTimeout ==
                                  TimeSpan.FromSeconds(120));

                TestHelper.Assert(IServicePrx.Parse("ice+tcp://localhost/identity", comm2).InvocationTimeout ==
                                  TimeSpan.FromSeconds(60));

                TestHelper.Assert(IServicePrx.Parse("ice+tcp://localhost/identity?invocation-timeout=10s",
                                                   comm1).InvocationTimeout == TimeSpan.FromSeconds(10));

                TestHelper.Assert(IServicePrx.Parse("ice+tcp://localhost/identity?invocation-timeout=10s",
                                                   comm2).InvocationTimeout == TimeSpan.FromSeconds(10));

                TestHelper.Assert(IServicePrx.Parse("identity -t:tcp -h localhost", comm1).InvocationTimeout ==
                                 TimeSpan.FromSeconds(120));

                TestHelper.Assert(IServicePrx.Parse("identity -t:tcp -h localhost", comm2).InvocationTimeout ==
                                  TimeSpan.FromSeconds(60));
            }
            output.WriteLine("ok");

            output.Write("testing invalid invocation timeout... ");
            output.Flush();
            {
                try
                {
                    await using var comm1 = new Communicator(new Dictionary<string, string>()
                    {
                        { "Ice.Default.InvocationTimeout", "0s" }
                    });
                    TestHelper.Assert(false);
                }
                catch (InvalidConfigurationException)
                {
                }

                try
                {
                    IServicePrx.Parse("ice+tcp://localhost/identity", communicator).Clone(
                        invocationTimeout: TimeSpan.Zero);
                    TestHelper.Assert(false);
                }
                catch (ArgumentException)
                {
                }
            }
            output.WriteLine("ok");

            await cl.ShutdownAsync();
        }
    }

    internal delegate int CallbackDelegate(IRelativeTestPrx relativeTest, Current current, CancellationToken cancel);

    internal sealed class Callback : ICallback
    {
        private CallbackDelegate _delegate;

        public int Op(IRelativeTestPrx relativeTest, Current current, CancellationToken cancel) =>
            _delegate(relativeTest, current, cancel);

        internal Callback(CallbackDelegate @delegate) => _delegate = @delegate;
    }
}
