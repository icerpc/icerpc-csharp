// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Proxy
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoint = GetTestEndpoint(0) });

            server.Add("test", new MyDerivedClass());
            // Don't activate Server to ensure collocation is used.

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
