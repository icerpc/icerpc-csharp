// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IceRpc.Test.Binding
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var router = new Router();
            router.Map("/communicator", new RemoteCommunicator());

            await using var server = new Server
            {
                Communicator = Communicator,
                Dispatcher = router,
                Endpoint = GetTestEndpoint(0),
                ProxyHost = TestHelper.GetTestHost(Communicator.GetProperties())
            };

            server.Listen();
            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.ServerIdleTime"] = "30";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
