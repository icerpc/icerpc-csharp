// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Retry
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server1 = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            server1.Add("retry", new Retry());
            server1.Add("replicated", new Replicated(true));
            server1.Add("nonreplicated", new NonReplicated());
            await server1.ActivateAsync();

            await using var server2 = new Server(Communicator, new() { Endpoints = GetTestEndpoint(1) });
            server2.Add("replicated", new Replicated(false));
            await server2.ActivateAsync();

            ServerReady();
            await server1.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            var properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";
            properties["Ice.Warn.Connections"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
