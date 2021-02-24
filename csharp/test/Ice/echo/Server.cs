// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Echo
{
    public class ServerApp : TestHelper
    {
        private class Echo : IEcho
        {
            public void Shutdown(Current current, CancellationToken cancel) =>
                current.Adapter.ShutdownAsync();
        }

        public override async Task RunAsync(string[] args)
        {
            await using var adapter = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            var blob = new BlobjectI();
            adapter.AddDefault(blob);
            adapter.Add("__echo", new Echo());
            await adapter.ActivateAsync();

            ServerReady();
            await adapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
