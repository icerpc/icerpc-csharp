// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Operations
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var adapter = new ObjectAdapter(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            adapter.Add("test", new MyDerivedClass());
            await adapter.ActivateAsync();

            ServerReady();
            await adapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            var properties = CreateTestProperties(ref args);
            // We don't want connection warnings because of the timeout test.
            properties["Ice.Warn.Connections"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<Server>(communicator, args);
        }
    }
}
