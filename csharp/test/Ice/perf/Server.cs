// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Threading.Tasks;

namespace IceRpc.Test.Perf
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server
            {
                Communicator = Communicator,
                Dispatcher = new PerformanceI(),
                Endpoint = GetTestEndpoint(0)
            };

            server.Listen();
            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
