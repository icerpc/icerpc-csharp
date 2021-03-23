// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Binding
{
    public class RemoteCommunicator : IAsyncRemoteCommunicator
    {
        private int _nextPort = 10;

        public ValueTask<IRemoteServerPrx> CreateServerAsync(
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

                    var server = new Server(
                        current.Communicator,
                        new()
                        {
                            ConnectionOptions = new()
                            {
                                AcceptNonSecure = transport == "udp" ? NonSecure.Always :
                                    current.Communicator.GetPropertyAsEnum<NonSecure>("Ice.AcceptNonSecure") ??
                                        NonSecure.Always,
                            },
                            Endpoints = endpoints,
                            Name = name,
                            PublishedHost = TestHelper.GetTestHost(current.Communicator.GetProperties())
                        });
                    server.Activate();

                    return new(current.Server.AddWithUUID(new RemoteServer(server), IRemoteServerPrx.Factory));
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

        public ValueTask<IRemoteServerPrx> CreateServerWithEndpointsAsync(
            string name,
            string endpoints,
            Current current,
            CancellationToken cancel)
        {
            var server = new Server(
                current.Communicator,
                new ServerOptions { Endpoints = endpoints, Name = name });
            server.Activate();

            return new(current.Server.AddWithUUID(new RemoteServer(server), IRemoteServerPrx.Factory));
        }

        // Colocated call.
        public ValueTask DeactivateServerAsync(
            IRemoteServerPrx server,
            Current current,
            CancellationToken cancel) =>
            new(server.DeactivateAsync(cancel: cancel));

        public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
        {
            _ = current.Server.ShutdownAsync(); // only initiate shutdown
            return default;
        }
    }
}
