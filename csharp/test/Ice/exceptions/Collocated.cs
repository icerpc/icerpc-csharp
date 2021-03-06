// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Exceptions
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            server.Add("thrower", new Thrower());

            await AllTests.RunAsync(this);
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Connections"] = "0";
            properties["Ice.Warn.Dispatch"] = "0";
            properties["Ice.IncomingFrameMaxSize"] = "10K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
