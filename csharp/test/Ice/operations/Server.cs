// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Operations
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            server.Add("test", new MyDerivedClass());
            await server.ActivateAsync();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            var properties = CreateTestProperties(ref args);
            // We don't want connection warnings because of the timeout test.
            properties["Ice.Warn.Connections"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
