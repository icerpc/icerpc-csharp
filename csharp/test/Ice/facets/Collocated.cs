// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Facets
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoint = GetTestEndpoint(0) });

            var d = new D();
            server.Add("d", d);
            server.Add("d", "facetABCD", d);
            server.Add("d", "facetEF", new F());
            server.Add("d", "facetGH", new H());

            await AllTests.RunAsync(this);
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
