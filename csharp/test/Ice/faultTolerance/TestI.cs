// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Threading;

namespace ZeroC.Ice.Test.FaultTolerance
{
    public sealed class TestIntf : ITestIntf
    {
        public void Abort(Current current, CancellationToken cancel)
        {
            Process.GetCurrentProcess().Kill();
            // Kill is asynchronous so we call wait for exit to ensure this call doesn't return successfully.
            Process.GetCurrentProcess().WaitForExit();
        }

        public int Pid(Current current, CancellationToken cancel) => System.Environment.ProcessId;

        public void Shutdown(Current current, CancellationToken cancel) => _ = current.Adapter.ShutdownAsync();
    }
}
