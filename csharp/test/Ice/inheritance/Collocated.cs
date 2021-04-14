// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Threading.Tasks;

namespace IceRpc.Test.Inheritance
{
    public class Collocated : TestHelper
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

            _ = server.ListenAndServeAsync();

            await AllTests.RunAsync(this);
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
