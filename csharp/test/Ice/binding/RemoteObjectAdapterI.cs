// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.Binding
{
    public class RemoteServer : IAsyncRemoteServer
    {
        private readonly Server _server;

        private readonly ITestIntfPrx _testIntf;

        public RemoteServer(Server server, string serverName)
        {
            _server = server;
            _testIntf = _server.Add("/test", new TestIntf(serverName), ITestIntfPrx.Factory);
        }

        public ValueTask<ITestIntfPrx> GetTestIntfAsync(Current current, CancellationToken cancel) =>
            new(_testIntf);

        public ValueTask DeactivateAsync(Current current, CancellationToken cancel) => new(_server.ShutdownAsync());
    }
}
