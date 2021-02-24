// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.DictMapping
{
    public class ServerAppAMD : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            server.Add("test", new AsyncMyClass());
            await server.ActivateAsync();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerAppAMD>(communicator, args);
        }
    }
}
