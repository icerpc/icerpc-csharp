// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Proxy
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
