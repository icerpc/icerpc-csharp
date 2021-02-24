// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.AdapterDeactivation
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var server = new Server(Communicator,
                                            new() { Endpoints = GetTestEndpoint(0) });

            server.AddDefault(new Servant());
            await server.ActivateAsync();

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
