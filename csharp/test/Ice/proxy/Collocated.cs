// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IceRpc.Test.Proxy
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var router = new Router();
            router.Map("/test", new MyDerivedClass());

            await using var server = new Server()
            {
                Communicator = Communicator,
                Dispatcher = router,
                Endpoint = GetTestEndpoint(0)
            };
            _ = server.ListenAndServeAsync();

            await AllTests.RunAsync(this);
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
