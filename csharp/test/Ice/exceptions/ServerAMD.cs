// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Exceptions
{
    public class ServerAppAMD : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            Server server2 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(1), IncomingFrameMaxSize = 0 });

            Server server3 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(2), IncomingFrameMaxSize = 1024 });

            var obj = new AsyncThrower();
            IceRpc.IServicePrx prx = server.Add("thrower", obj, IceRpc.IServicePrx.Factory);
            server2.Add("thrower", obj);
            server3.Add("thrower", obj);
            await server.ActivateAsync();
            await server2.ActivateAsync();
            await server3.ActivateAsync();

            await using var communicator2 = new Communicator(Communicator.GetProperties());

            await using var forwarderAdapter = new Server(
                communicator2,
                new() { Endpoints = GetTestEndpoint(3), IncomingFrameMaxSize = 0 });

            forwarderAdapter.Add("forwarder", new Forwarder(IServicePrx.Parse(GetTestProxy("thrower"), communicator2)));
            await forwarderAdapter.ActivateAsync();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";
            properties["Ice.Warn.Connections"] = "0";
            properties["Ice.IncomingFrameMaxSize"] = "10K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerAppAMD>(communicator, args);
        }
    }
}
