// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.AdapterDeactivation
{
    public sealed class TestIntf : IAsyncTestIntf
    {
        public async ValueTask TransientAsync(Current current, CancellationToken cancel)
        {
            bool ice1 = TestHelper.GetTestProtocol(current.Communicator.GetProperties()) == Protocol.Ice1;
            var transport = TestHelper.GetTestTransport(current.Communicator.GetProperties());
            var endpoint = ice1 ? $"{transport} -h \"::0\"" : $"ice+{transport}://[::0]:0";

            await using ObjectAdapter adapter =
                current.Communicator.CreateObjectAdapter(
                    "TransientTestAdapter",
                    new ObjectAdapterOptions { Endpoints = endpoint });
            await adapter.ActivateAsync(cancel);
        }

        public async ValueTask DeactivateAsync(Current current, CancellationToken cancel)
        {
            _ = current.Adapter.DisposeAsync().AsTask();
            await Task.Delay(100, cancel);
            _ = current.Communicator.ShutdownAsync();
        }
    }
}
