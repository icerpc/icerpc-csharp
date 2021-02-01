// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.ACM
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            ObjectAdapter adapter = Communicator.CreateObjectAdapter(
                "TestAdapter",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(0) });

            adapter.Add("communicator", new RemoteCommunicator());
            await adapter.ActivateAsync();

            ServerReady();
            Communicator.SetProperty("Ice.PrintAdapterReady", "0");
            await Communicator.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Server>(communicator, args);
        }
    }
}
