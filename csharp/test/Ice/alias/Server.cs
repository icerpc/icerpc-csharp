// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Alias
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server
            {
                Communicator = Communicator,
                Endpoint = GetTestEndpoint(0)
            };

            server.Add("/test", new Interface2());

            Task shutdownComplete = server.ListenAndServeAsync();
            ServerReady();
            await shutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
