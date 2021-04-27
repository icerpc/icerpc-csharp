// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;

namespace IceRpc.Test.Binding
{
    public class TestIntf : ITestIntf
    {
        private string _serverName;

        public string GetAdapterName(Dispatch dispatch, CancellationToken cancel) => _serverName;

        internal TestIntf(string serverName) => _serverName = serverName;
    }
}
