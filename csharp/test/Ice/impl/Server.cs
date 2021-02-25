// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Reflection;
using System.Threading.Tasks;
using ZeroC.Test;

[assembly: AssemblyTitle("IceTest")]
[assembly: AssemblyDescription("Ice test")]
[assembly: AssemblyCompany("ZeroC, Inc.")]

namespace ZeroC.Ice.Test.Impl
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            // We don't want connection warnings because of the timeout test.
            Communicator.getProperties().setProperty("Ice.Warn.Connections", "0");
            Communicator.getProperties().setProperty("TestAdapter.Endpoints", getTestEndpoint(0));

            Ice.Server server = Communicator.createServer("TestAdapter");
            server.add(Ice.Util.stringToIdentity("test"), new MyDerivedClassI());
            server.activate();

            ServerReady();
            Communicator.waitForShutdown();
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
