// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Perf
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var adapter = new ObjectAdapter(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            adapter.Add("perf", new PerformanceI());
            await adapter.ActivateAsync();

            ServerReady();
            await adapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Server>(communicator, args);
        }
    }
}
