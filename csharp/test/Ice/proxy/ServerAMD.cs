// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Threading.Tasks;

namespace IceRpc.Test.Proxy
{
    public class ServerAppAMD : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var router = new Router();
            router.Map("/test", new AsyncMyDerivedClass());

            await using var server = new Server()
            {
                Communicator = Communicator,
                Dispatcher = router,
                Endpoint = GetTestEndpoint(0)
            };

            Task shutdownComplete = server.ListenAndServeAsync();
            ServerReady();
            await shutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            var properties = CreateTestProperties(ref args);
            // We don't want connection warnings because of the timeout test.
            properties["Ice.Warn.Connections"] = "0";
            properties["Ice.Warn.Dispatch"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerAppAMD>(communicator, args);
        }
    }
}
