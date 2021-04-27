// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Test.Binding
{
    public class TestIntf : IAsyncTestIntf
    {
        private string _serverName;

        public ValueTask<string> GetAdapterNameAsync(Dispatch dispatch, CancellationToken cancel) => new(_serverName);

        internal TestIntf(string serverName) => _serverName = serverName;
    }
}
