// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.DictMapping
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoint = GetTestEndpoint(0) });

            server.Add("test", new MyClass());
            server.Activate();

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
