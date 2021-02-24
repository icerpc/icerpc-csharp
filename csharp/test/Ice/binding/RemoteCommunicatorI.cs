// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Binding
{
    public class RemoteCommunicator : IAsyncRemoteCommunicator
    {
        private int _nextPort = 10;

        public async ValueTask<IRemoteServerPrx> CreateServerAsync(
            string name,
            string transport,
            Current current,
            CancellationToken cancel)
        {
            int retry = 5;
            while (true)
            {
                try
                {
                    string endpoints =
                        TestHelper.GetTestEndpoint(current.Communicator.GetProperties(), _nextPort++, transport);

                    var adapter = new Server(
                        current.Communicator,
                        new()
                        {
                            AcceptNonSecure = transport == "udp" ? NonSecure.Always :
                                current.Communicator.GetPropertyAsEnum<NonSecure>("Ice.AcceptNonSecure") ??
                                    NonSecure.Always,
                            Endpoints = endpoints,
                            Name = name,
                            ServerName = TestHelper.GetTestHost(current.Communicator.GetProperties())
                        });
                    await adapter.ActivateAsync(cancel);

                    return current.Adapter.AddWithUUID(new RemoteServer(adapter),
                                                       IRemoteServerPrx.Factory);
                }
                catch (TransportException)
                {
                    if (--retry == 0)
                    {
                        throw;
                    }
                }
            }
        }

        public async ValueTask<IRemoteServerPrx> CreateServerWithEndpointsAsync(
            string name,
            string endpoints,
            Current current,
            CancellationToken cancel)
        {
            var adapter = new Server(
                current.Communicator,
                new ServerOptions { Endpoints = endpoints, Name = name });
            await adapter.ActivateAsync(cancel);

            return current.Adapter.AddWithUUID(new RemoteServer(adapter), IRemoteServerPrx.Factory);
        }

        // Colocated call.
        public ValueTask DeactivateServerAsync(
            IRemoteServerPrx adapter,
            Current current,
            CancellationToken cancel) =>
            new(adapter.DeactivateAsync(cancel: cancel));

        public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
        {
            _ = current.Adapter.ShutdownAsync(); // only initiate shutdown
            return default;
        }
    }
}
