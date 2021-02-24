// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Exceptions
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var adapter = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            Server adapter2 = new Server(Communicator,
                new() { Endpoints = GetTestEndpoint(1), IncomingFrameMaxSize = 0 });

            Server adapter3 = new Server(Communicator,
                new() { Endpoints = GetTestEndpoint(2), IncomingFrameMaxSize = 1024 });

            var obj = new Thrower();
            ZeroC.Ice.IServicePrx prx = adapter.Add("thrower", obj, ZeroC.Ice.IServicePrx.Factory);
            adapter2.Add("thrower", obj);
            adapter3.Add("thrower", obj);
            await adapter.ActivateAsync();
            await adapter2.ActivateAsync();
            await adapter3.ActivateAsync();

            await using var communicator2 = new Communicator(Communicator.GetProperties());
            await using var forwarderAdapter = new Server(
                communicator2,
                new() { Endpoints = GetTestEndpoint(3), IncomingFrameMaxSize = 0 });
            forwarderAdapter.Add("forwarder", new Forwarder(IServicePrx.Parse(GetTestProxy("thrower"), communicator2)));
            await forwarderAdapter.ActivateAsync();

            ServerReady();
            await adapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";
            properties["Ice.Warn.Connections"] = "0";
            properties["Ice.IncomingFrameMaxSize"] = "10K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
