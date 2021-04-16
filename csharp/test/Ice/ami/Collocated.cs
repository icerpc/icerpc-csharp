// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IceRpc.Test.AMI
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var router = new Router();
            router.Map("/test", new TestIntf());
            router.Map("/test2", new TestIntf2());

            await using var server = new Server
            {
                Communicator = Communicator,
                Dispatcher = router,
                Endpoint = GetTestEndpoint(0)
            };

            server.Listen();

            await AllTests.RunAsync(this, true);
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);

            // Limit the send buffer size, this test relies on the socket send() blocking after sending a given amount
            // of data.
            properties["Ice.TCP.SndSize"] = "50K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
