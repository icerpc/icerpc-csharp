// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;
using ZeroC.Ice.Discovery;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Discovery
{
    public class Client : TestHelper
    {
        public override Task RunAsync(string[] args)
        {
            int num;
            try
            {
                num = args.Length == 1 ? int.Parse(args[0]) : 0;
            }
            catch (FormatException)
            {
                num = 0;
            }

            return AllTests.RunAsync(this, num);
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);

            // TODO: convert properties to options for now
            var discoveryServerOptions = new DiscoveryServerOptions
            {
                DomainId = communicator.GetProperty("Ice.Discovery.DomainId") ?? "",
                Lookup = communicator.GetProperty("Ice.Discovery.Lookup") ?? "",
                MulticastEndpoints = communicator.GetProperty("Ice.Discovery.Multicast.Endpoints") ?? "",
                RetryCount = communicator.GetPropertyAsInt("Ice.Discovery.RetryCount") ?? 20,
                ReplyServerName = communicator.GetProperty("Ice.Discovery.Reply.ServerName") ?? "",
                Timeout = communicator.GetPropertyAsTimeSpan("Ice.Discovery.Timeout") ?? TimeSpan.FromMilliseconds(100)
            };

            await using var discoveryServer = new DiscoveryServer(communicator, discoveryServerOptions);
            await discoveryServer.ActivateAsync();

            return await RunTestAsync<Client>(communicator, args);
        }
    }
}
