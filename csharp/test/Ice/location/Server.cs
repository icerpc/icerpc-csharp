// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Location
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            // Register the server manager. The server manager creates a new 'server' (a server isn't a different
            // process, it's just a new communicator and server).
            await using var server = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            // We also register a sample server locator which implements the locator interface, this locator is used by
            // the clients and the 'servers' created with the server manager interface.
            var registry = new ServerLocatorRegistry();
            var obj = new ServerManager(registry, this);
            server.Add("ServerManager", obj);
            registry.AddObject(server.CreateProxy("ServerManager", IServicePrx.Factory));
            ILocatorRegistryPrx registryPrx = server.Add("registry", registry, ILocatorRegistryPrx.Factory);
            server.Add("locator", new ServerLocator(registry, registryPrx));
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
