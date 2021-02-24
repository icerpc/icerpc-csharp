// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.ACM
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            server.Add("communicator", new RemoteCommunicator());
            await server.ActivateAsync();

            ServerReady();
            Communicator.SetProperty("Ice.PrintAdapterReady", "0");
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
