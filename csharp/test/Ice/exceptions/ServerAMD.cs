// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Exceptions
{
    public class ServerAMD : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            ObjectAdapter adapter = Communicator.CreateObjectAdapter(
                "TestAdapter",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(0) });

            ObjectAdapter adapter2 = Communicator.CreateObjectAdapter(
                "TestAdapter2",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(1), IncomingFrameMaxSize = 0 });

            ObjectAdapter adapter3 = Communicator.CreateObjectAdapter(
                "TestAdapter3",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(2), IncomingFrameMaxSize = 1024 });

            var obj = new AsyncThrower();
            ZeroC.Ice.IObjectPrx prx = adapter.Add("thrower", obj, ZeroC.Ice.IObjectPrx.Factory);
            adapter2.Add("thrower", obj);
            adapter3.Add("thrower", obj);
            await adapter.ActivateAsync();
            await adapter2.ActivateAsync();
            await adapter3.ActivateAsync();

            await using var communicator2 = new Communicator(Communicator.GetProperties());

            ObjectAdapter forwarderAdapter = communicator2.CreateObjectAdapter(
                "ForwarderAdapter",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(3), IncomingFrameMaxSize = 0 });

            forwarderAdapter.Add("forwarder", new Forwarder(IObjectPrx.Parse(GetTestProxy("thrower"), communicator2)));
            await forwarderAdapter.ActivateAsync();

            ServerReady();
            await Communicator.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";
            properties["Ice.Warn.Connections"] = "0";
            properties["Ice.IncomingFrameMaxSize"] = "10K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerAMD>(communicator, args);
        }
    }
}
