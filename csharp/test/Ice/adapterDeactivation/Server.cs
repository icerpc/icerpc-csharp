// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.AdapterDeactivation
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            ObjectAdapter adapter = Communicator.CreateObjectAdapter(
                "TestAdapter",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(0) });

            adapter.AddDefault(new Servant());
            await adapter.ActivateAsync();

            ServerReady();
            await Communicator.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Server>(communicator, args);
        }
    }
}
