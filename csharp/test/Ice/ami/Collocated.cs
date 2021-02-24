// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.AMI
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var adapter = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            adapter.Add("test", new TestIntf());
            adapter.Add("test2", new TestIntf2());
            // Don't activate OA to ensure collocation is used.

            Server adapter2 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(1), SerializeDispatch = true });

            adapter2.Add("serialized", new TestIntf());
            // Don't activate OA to ensure collocation is used.

            await AllTests.RunAsync(this);
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
