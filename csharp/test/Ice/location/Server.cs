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
            // process, it's just a new communicator and object adapter).
            await using var adapter = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            // We also register a sample server locator which implements the locator interface, this locator is used by
            // the clients and the 'servers' created with the server manager interface.
            var registry = new ServerLocatorRegistry();
            var obj = new ServerManager(registry, this);
            adapter.Add("ServerManager", obj);
            registry.AddObject(adapter.CreateProxy("ServerManager", IServicePrx.Factory));
            ILocatorRegistryPrx registryPrx = adapter.Add("registry", registry, ILocatorRegistryPrx.Factory);
            adapter.Add("locator", new ServerLocator(registry, registryPrx));
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
