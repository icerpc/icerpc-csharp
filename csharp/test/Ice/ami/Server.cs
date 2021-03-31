// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.AMI
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoint = GetTestEndpoint(0) });

            server.Add("test", new TestIntf());
            server.Add("test2", new TestIntf2());
            server.Activate();

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
