// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Invoke
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var adapter = new ObjectAdapter(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            adapter.AddDefault(new BlobjectI());
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
