// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.Binding
{
    public class RemoteServer : IRemoteServer
    {
        private readonly Server _server;

        private readonly ITestIntfPrx _testIntf;

        public RemoteServer(Server server)
        {
            _server = server;
            _testIntf = _server.CreateProxy<ITestIntfPrx>("/test");
        }

        public ValueTask<ITestIntfPrx> GetTestIntfAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(_testIntf);

        public ValueTask DeactivateAsync(Dispatch dispatch, CancellationToken cancel) => new(_server.ShutdownAsync());
    }
}
