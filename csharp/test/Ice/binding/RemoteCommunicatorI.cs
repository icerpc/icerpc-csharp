// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Test;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.Binding
{
    public class RemoteCommunicator : IAsyncRemoteCommunicator
    {
        private int _nextPort = 10;

        public ValueTask<IRemoteServerPrx> CreateServerAsync(
            string name,
            string transport,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            int retry = 5;
            while (true)
            {
                try
                {
                    string endpoint =
                        TestHelper.GetTestEndpoint(dispatch.Communicator.GetProperties(), _nextPort++, transport);

                    var server = new Server
                    {
                        Communicator = dispatch.Communicator,
                        Dispatcher = new TestIntf(name),
                        Endpoint = endpoint,
                        ProxyHost = TestHelper.GetTestHost(dispatch.Communicator.GetProperties())
                    };

                    server.Listen();

                    return new(TestHelper.AddWithGuid<IRemoteServerPrx>(dispatch.Server!, new RemoteServer(server)));
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
            Dispatch dispatch,
            CancellationToken cancel)
        {
            var server = new Server
            {
                Communicator = dispatch.Communicator,
                Dispatcher = new TestIntf(name),
                Endpoint = endpoints
            };

            server.Listen();
            return new(TestHelper.AddWithGuid<IRemoteServerPrx>(dispatch.Server!, new RemoteServer(server)));
        }

        // Coloc call.
        public ValueTask DeactivateServerAsync(
            IRemoteServerPrx server,
            Dispatch dispatch,
            CancellationToken cancel) =>
            new(server.DeactivateAsync(cancel: cancel));

        public ValueTask ShutdownAsync(Dispatch dispatch, CancellationToken cancel)
        {
            _ = dispatch.Server!.ShutdownAsync(); // only initiate shutdown
            return default;
        }
    }
}
