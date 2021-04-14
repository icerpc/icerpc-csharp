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
            var cl = await baseProxy.CheckedCastAsync<IMyClassPrx>();
            TestHelper.Assert(cl != null);
            var derived = await cl.CheckedCastAsync<IMyDerivedClassPrx>();
            TestHelper.Assert(derived != null);
            TestHelper.Assert(cl.Equals(baseProxy));
            TestHelper.Assert(derived.Equals(baseProxy));
            TestHelper.Assert(cl.Equals(derived));

            if (ice1)
            {
                try
                {
                    await cl.WithFacet<IMyDerivedClassPrx>("facet").IcePingAsync();
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
            cl = await baseProxy.CheckedCastAsync<IMyClassPrx>(c);
            SortedDictionary<string, string> c2 = cl!.GetContext();
            TestHelper.Assert(c.DictionaryEqual(c2));
            output.WriteLine("ok");

            if (ice1)
            {
                output.Write("testing ice2 proxy in 1.1 encapsulation... ");
                output.Flush();
                var ice2Prx = IServicePrx.Parse(
                    "ice+tcp://localhost:10000/foo?alt-endpoint=ice+ws://localhost:10000", communicator);
                var prx = baseProxy.As<IMyDerivedClassPrx>().Echo(ice2Prx);
                TestHelper.Assert(ice2Prx.Equals(prx));
                output.WriteLine("ok");
            }
            else
            {
                output.Write("testing ice1 proxy in 2.0 encapsulation... ");
                output.Flush();
                var ice1Prx = IServicePrx.Parse(
                    "foo:tcp -h localhost -p 10000:udp -h localhost -p 10000", communicator);

                var prx = baseProxy.As<IMyDerivedClassPrx>().Echo(ice1Prx);
                TestHelper.Assert(ice1Prx.Equals(prx));
                output.WriteLine("ok");
            }

            if (!ice1)
            {
                output.Write("testing relative proxies... ");
                {
                    await using Server server = new Server
                    {
                        Communicator = communicator
                    };
                    _ = server.ListenAndServeAsync();

                    (await cl.GetConnectionAsync()).Server = server;

                    // It's a non-fixed ice2 proxy with no endpoints, i.e. a relative proxy
                    ICallbackPrx callback = server.AddWithUUID(
                        new Callback((relativeTest, current, cancel) =>
                                     {
                                         TestHelper.Assert(relativeTest.Connection != null);
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

            output.Write("testing encoding versioning... ");
            string ref13 = helper.GetTestProxy("test", 0);
            IMyClassPrx cl13 = IMyClassPrx.Parse(ref13, communicator);
            cl13.Encoding = new Encoding(1, 3);
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
                TestHelper.Assert(IServicePrx.Parse("ice+tcp://localhost/identity", communicator).InvocationTimeout ==
                                  TimeSpan.FromSeconds(60));

                TestHelper.Assert(IServicePrx.Parse("ice+tcp://localhost/identity?invocation-timeout=10s",
                                                   communicator).InvocationTimeout == TimeSpan.FromSeconds(10));

                TestHelper.Assert(IServicePrx.Parse("identity -t:tcp -h localhost", communicator).InvocationTimeout ==
                                  TimeSpan.FromSeconds(60));
            }
            output.WriteLine("ok");

            output.Write("testing invalid invocation timeout... ");
            output.Flush();
            {
                try
                {
                    var prx = IServicePrx.Parse("ice+tcp://localhost/identity", communicator);
                    prx.InvocationTimeout = TimeSpan.Zero;
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
