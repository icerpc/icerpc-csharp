// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.AMI
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            server.Add("test", new TestIntf());
            server.Add("test2", new TestIntf2());
            await server.ActivateAsync();

            var server2 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(1), SerializeDispatch = true });

            server2.Add("serialized", new TestIntf());
            await server2.ActivateAsync();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            // This test kills connections, so we don't want warnings.
            properties["Ice.Warn.Connections"] = "0";
            // Limit the recv buffer size, this test relies on the socket send() blocking after sending a given amount
            // of data.
            properties["Ice.TCP.RcvSize"] = "50K";
            // The client sends large payloads to block in send()
            properties["Ice.IncomingFrameMaxSize"] = "15M";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
