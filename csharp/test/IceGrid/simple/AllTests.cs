// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ZeroC.Ice;
using ZeroC.Test;

namespace ZeroC.IceGrid.Test.Simple
{
    public static class AllTests
    {
        public static Task RunAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;
            TextWriter output = helper.Output;

            output.Write("testing stringToProxy... ");
            output.Flush();
            string rf = "test @ TestAdapter";
            var obj = ITestIntfPrx.Parse(rf, communicator);
            output.WriteLine("ok");

            output.Write("pinging server... ");
            output.Flush();
            obj.IcePing();
            output.WriteLine("ok");

            output.Write("testing locator finder... ");
            var finderId = new Identity("LocatorFinder", "Ice");
            ILocatorFinderPrx finder = communicator.DefaultLocator!.Clone(ILocatorFinderPrx.Factory,
                                                                          identity: finderId);
            TestHelper.Assert(finder != null && finder.GetLocator() != null);
            output.WriteLine("ok");

            output.Write("shutting down server... ");
            output.Flush();
            obj.Shutdown();
            output.WriteLine("ok");
            return Task.CompletedTask;
        }

        public static async Task RunWithDeployAsync(TestHelper helper)
        {
            Communicator communicator = helper.Communicator;
            TextWriter output = helper.Output;

            output.Write("testing stringToProxy... ");
            output.Flush();
            var obj = ITestIntfPrx.Parse("test @ TestAdapter", communicator);
            var obj2 = ITestIntfPrx.Parse("test", communicator);
            output.WriteLine("ok");

            output.Write("pinging server... ");
            output.Flush();
            obj.IcePing();
            obj2.IcePing();
            output.WriteLine("ok");

            output.Write("testing encoding versioning... ");
            output.Flush();
            output.WriteLine("ok");

            output.Write("testing reference with unknown identity... ");
            output.Flush();
            try
            {
                IObjectPrx.Parse("unknown/unknown", communicator).IcePing();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
                // expected
            }
            output.WriteLine("ok");

            output.Write("testing reference with unknown adapter... ");
            output.Flush();
            try
            {
                IObjectPrx.Parse("test @ TestAdapterUnknown", communicator).IcePing();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
                // expected
            }
            output.WriteLine("ok");

            var registry = IRegistryPrx.Parse(
                $"{communicator.DefaultLocator!.Identity.Category}/Registry", communicator);
            IAdminSessionPrx? session = registry.CreateAdminSession("foo", "bar");
            TestHelper.Assert(session != null);
            Connection connection = await session.GetConnectionAsync();
            connection.KeepAlive = true;

            IAdminPrx? admin = session.GetAdmin();
            TestHelper.Assert(admin != null);
            admin.EnableServer("server", false);
            admin.StopServer("server");

            output.Write("testing whether server is still reachable... ");
            output.Flush();
            try
            {
                obj.IcePing();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }
            try
            {
                obj2.IcePing();
                TestHelper.Assert(false);
            }
            catch (NoEndpointException)
            {
            }

            admin.EnableServer("server", true);
            obj.IcePing();
            obj2.IcePing();
            output.WriteLine("ok");

            admin.StopServer("server");
            session.Destroy();
        }
    }
}
