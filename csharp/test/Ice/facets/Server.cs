// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Facets
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            var d = new D();
            server.Add("/d", d);
            server.Add("/d", "facetABCD", d);
            var f = new F();
            server.Add("/d", "facetEF", f);
            var h = new H();
            server.Add("/d", "facetGH", h);
            server.Activate();

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
