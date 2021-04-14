// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Threading.Tasks;

namespace IceRpc.Test.Facets
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            IDispatcher d = new D();
            IDispatcher f = new F();
            IDispatcher h = new H();

            var dispatcher = new InlineDispatcher((current, cancel) =>
            {
                if (current.Path != "/d")
                {
                    throw new ServiceNotFoundException();
                }

                return current.IncomingRequestFrame.Facet switch
                {
                    "" => d.DispatchAsync(current, cancel),
                    "facetABCD" => d.DispatchAsync(current, cancel),
                    "facetEF" => f.DispatchAsync(current, cancel),
                    "facetGH" => h.DispatchAsync(current, cancel),
                    _ => throw new ServiceNotFoundException()
                };
            });

            await using var server = new Server
            {
                Communicator = Communicator,
                Dispatcher = dispatcher,
                Endpoint = GetTestEndpoint(0)
            };

            _ = server.ListenAndServeAsync();

            await AllTests.RunAsync(this);
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
