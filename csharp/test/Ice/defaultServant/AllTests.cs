// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.DefaultServant
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper)
        {
            TextWriter output = helper.Output;
            Communicator communicator = helper.Communicator;

            await using var oa =
                new Server(communicator, new() { Endpoints = helper.GetTestEndpoint(ephemeral: true) });
            await oa.ActivateAsync();

            output.Write("testing single category... ");
            output.Flush();

            var servant = new MyObject();

            oa.AddDefaultForCategory("foo", servant);
            try
            {
                oa.AddDefaultForCategory("foo", new MyObject());
                TestHelper.Assert(false); // duplicate registration not allowed
            }
            catch (ArgumentException)
            {
                // Expected
            }

            IService? r = oa.Find("foo");
            TestHelper.Assert(r == null);
            r = oa.Find("foo/someId");
            TestHelper.Assert(r == servant);
            r = oa.Find("bar/someId");
            TestHelper.Assert(r == null);

            var identity = new Identity("", "foo");
            string[] names = new string[] { "foo", "bar", "x", "y", "abcdefg" };

            IMyObjectPrx? prx = null;
            foreach (string name in names)
            {
                identity = new Identity(name, identity.Category);
                prx = IMyObjectPrx.Factory.Create(oa, identity.ToPath());
                await prx.IcePingAsync();
                TestHelper.Assert(prx.GetName() == $"/foo/{name}");
            }

            identity = new Identity("ObjectNotExist", identity.Category);
            prx = IMyObjectPrx.Factory.Create(oa, identity.ToPath());
            try
            {
                await prx.IcePingAsync();
                TestHelper.Assert(false);
            }
            catch (ServiceNotFoundException)
            {
                // Expected
            }

            try
            {
                prx.GetName();
                TestHelper.Assert(false);
            }
            catch (ServiceNotFoundException)
            {
                // Expected
            }

            identity = new Identity(identity.Name, "bar");
            foreach (string name in names)
            {
                identity = new Identity(name, identity.Category);
                prx = IMyObjectPrx.Factory.Create(oa, identity.ToPath());

                try
                {
                    await prx.IcePingAsync();
                    TestHelper.Assert(false);
                }
                catch (ServiceNotFoundException)
                {
                    // Expected
                }

                try
                {
                    prx.GetName();
                    TestHelper.Assert(false);
                }
                catch (ServiceNotFoundException)
                {
                    // Expected
                }
            }

            IService? removed = oa.RemoveDefaultForCategory("foo");
            TestHelper.Assert(removed == servant);
            removed = oa.RemoveDefaultForCategory("foo");
            TestHelper.Assert(removed == null);
            identity = new Identity(identity.Name, "foo");
            prx = IMyObjectPrx.Factory.Create(oa, identity.ToPath());
            try
            {
                await prx.IcePingAsync();
            }
            catch (ServiceNotFoundException)
            {
                // Expected
            }

            output.WriteLine("ok");

            output.Write("testing default servant... ");
            output.Flush();

            var defaultServant = new MyObject();

            oa.AddDefault(defaultServant);
            try
            {
                oa.AddDefault(servant);
                TestHelper.Assert(false);
            }
            catch (ArgumentException)
            {
                // Expected
            }

            oa.AddDefaultForCategory("", servant); // associated with empty category

            r = oa.Find("bar");
            TestHelper.Assert(r == servant);

            r = oa.Find("x/y");
            TestHelper.Assert(r == defaultServant);

            foreach (string name in names)
            {
                identity = new Identity(name, "");
                prx = IMyObjectPrx.Factory.Create(oa, identity.ToPath());
                await prx.IcePingAsync();
                TestHelper.Assert(prx.GetName() == $"/{name}");
            }

            removed = oa.RemoveDefault();
            TestHelper.Assert(removed == defaultServant);
            removed = oa.RemoveDefault();
            TestHelper.Assert(removed == null);

            output.WriteLine("ok");
        }
    }
}
