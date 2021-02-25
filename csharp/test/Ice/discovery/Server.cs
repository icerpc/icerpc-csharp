// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;
using ZeroC.Ice.Discovery;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Discovery
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            int num = 0;
            try
            {
                num = int.Parse(args[0]);
            }
            catch (FormatException)
            {
            }

            // TODO: convert properties to options for now
            var discoveryServerOptions = new DiscoveryServerOptions
            {
                DomainId = Communicator.GetProperty("Ice.Discovery.DomainId") ?? "",
                Lookup = Communicator.GetProperty("Ice.Discovery.Lookup") ?? "",
                MulticastEndpoints = Communicator.GetProperty("Ice.Discovery.Multicast.Endpoints") ?? "",
                RetryCount = Communicator.GetPropertyAsInt("Ice.Discovery.RetryCount") ?? 20,
                ReplyPublishedHost = Communicator.GetProperty("Ice.Discovery.Reply.PublishedHost") ?? "",
                Timeout = Communicator.GetPropertyAsTimeSpan("Ice.Discovery.Timeout") ?? TimeSpan.FromMilliseconds(100)
            };

            await using var discoveryServer = new DiscoveryServer(Communicator, discoveryServerOptions);
            Communicator.DefaultLocationService = new LocationService(discoveryServer.Locator);
            await discoveryServer.ActivateAsync();

            ILocatorRegistryPrx? locatorRegistry = await discoveryServer.Locator.GetRegistryAsync();
            TestHelper.Assert(locatorRegistry != null);

            await using var server = new Server(
                Communicator,
                new()
                {
                    AdapterId = $"control{num}",
                    Endpoints = GetTestEndpoint(num),
                    LocatorRegistry = locatorRegistry
                });

            server.Add($"controller{num}", new Controller(locatorRegistry));
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
