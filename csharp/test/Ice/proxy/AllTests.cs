// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using IceRpc.Test;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
                    var dispatcher = new Callback((relativeTest, current, cancel) =>
                    {
                        TestHelper.Assert(relativeTest.Connection != null);
                        TestHelper.Assert(current.Path == "/foo/bar");
                        return relativeTest.DoIt(cancel: cancel);
                    });

                    await using Server server = new Server
                    {
                        Communicator = communicator,
                        Dispatcher = dispatcher,
                    };
                    _ = server.ListenAndServeAsync();

                    (await cl.GetConnectionAsync()).Server = server;

                    // It's a non-fixed ice2 proxy with no endpoints, i.e. a relative proxy
                    ICallbackPrx callback = server.CreateRelativeProxy<ICallbackPrx>("/foo/bar");

                    await callback.IcePingAsync(); // colocated call

                    IRelativeTestPrx relativeTest = cl.OpRelative(callback);

                    TestHelper.Assert(relativeTest.Endpoints == cl.Endpoints); // reference equality
                    TestHelper.Assert(relativeTest.DoIt() == 2);
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
