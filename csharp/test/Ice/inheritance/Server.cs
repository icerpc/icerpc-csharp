// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Inheritance
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

            var initial = new InitialI(server);
            server.Add("/initial", initial);

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
