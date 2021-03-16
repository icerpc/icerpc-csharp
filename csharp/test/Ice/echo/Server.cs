// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Echo
{
    public class ServerApp : TestHelper
    {
        private class Echo : IEcho
        {
            public void Shutdown(Current current, CancellationToken cancel) =>
                current.Server.ShutdownAsync();
        }

        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            var blob = new BlobjectI();
            server.AddDefault(blob);
            server.Add("__echo", new Echo());
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
