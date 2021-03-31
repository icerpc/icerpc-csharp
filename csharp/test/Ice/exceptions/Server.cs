// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Exceptions
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(
                Communicator,
                new()
                {
                    ConnectionOptions = new() { IncomingFrameMaxSize = 10 * 1024 },
                    Endpoint = GetTestEndpoint(0)
                });

            Server server2 = new Server(
                Communicator,
                new()
                {
                    ConnectionOptions = new() { IncomingFrameMaxSize = int.MaxValue },
                    Endpoint = GetTestEndpoint(1),
                });

            Server server3 = new Server(
                Communicator,
                new()
                {
                    ConnectionOptions = new() { IncomingFrameMaxSize = 1024 },
                    Endpoint = GetTestEndpoint(2),
                });

            var obj = new Thrower();
            IceRpc.IServicePrx prx = server.Add("thrower", obj, IceRpc.IServicePrx.Factory);
            server2.Add("thrower", obj);
            server3.Add("thrower", obj);
            server.Activate();
            server2.Activate();
            server3.Activate();

            await using var communicator2 = new Communicator(Communicator.GetProperties());
            await using var forwarderAdapter = new Server(
                communicator2,
                new()
                {
                    ConnectionOptions = new() { IncomingFrameMaxSize = int.MaxValue },
                    Endpoint = GetTestEndpoint(3),
                });
            forwarderAdapter.Add("forwarder", new Forwarder(IServicePrx.Parse(GetTestProxy("thrower"), communicator2)));
            forwarderAdapter.Activate();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";
            properties["Ice.Warn.Connections"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
