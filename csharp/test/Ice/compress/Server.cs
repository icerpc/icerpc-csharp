// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Compress
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var adapter = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            adapter.Add("test-1", new Interceptor(new TestIntf(), compressed: true));
            adapter.Add("test-2", new Interceptor(new TestIntf(), compressed: false));
            await adapter.ActivateAsync();

            ServerReady();
            await adapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.CompressionMinSize"] = "1K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
