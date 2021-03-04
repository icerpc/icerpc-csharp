// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;

namespace IceRpc.Test.Binding
{
    public class TestIntf : ITestIntf
    {
        public string GetAdapterName(Current current, CancellationToken cancel) => current.Server.Name;
    }
}
