// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Objects
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            server.Add("initial", new Initial(server));
            server.Add("F21", new F2());
            var uoet = new UnexpectedObjectExceptionTest();
            server.Add("uoet", uoet);
            server.Activate();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
