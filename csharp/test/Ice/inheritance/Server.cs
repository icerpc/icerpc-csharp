// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Threading.Tasks;

namespace IceRpc.Test.Inheritance
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var router = new Router();

            await using var server = new Server
            {
                Communicator = Communicator,
                Dispatcher = router,
                Endpoint = GetTestEndpoint(0)
            };

            router.Map("/initial", new InitialI(server));

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
