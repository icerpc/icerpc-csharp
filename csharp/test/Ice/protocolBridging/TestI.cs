// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.ProtocolBridging
{
    public sealed class Forwarder : IService
    {
        private IServicePrx _target;

        ValueTask<OutgoingResponseFrame> IService.DispatchAsync(Current current, CancellationToken cancel)
        {
            if (current.Operation == "op" || current.Operation == "opVoid")
            {
                TestHelper.Assert(current.Context.Count == 1);
                TestHelper.Assert(current.Context["MyCtx"] == "hello");

                if (current.Operation == "opVoid")
                {
                    current.Context.Clear();
                }
            }
            else
            {
                TestHelper.Assert(current.Context.Count == 0);
            }

            if (current.Operation != "opVoid" && current.Operation != "shutdown")
            {
                current.Context["Intercepted"] = "1";
            }

            return _target.ForwardAsync(current.IncomingRequestFrame, current.IsOneway, cancel: cancel);
        }

        internal Forwarder(IServicePrx target) => _target = target;
    }

    public sealed class TestI : ITestIntf
    {
        public int Op(int x, Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.Context["MyCtx"] == "hello");
            TestHelper.Assert(current.Context.Count == 2);
            TestHelper.Assert(current.Context.ContainsKey("Intercepted") || current.Context.ContainsKey("Direct"));
            return x;
        }

        public void OpVoid(Current current, CancellationToken cancel)
        {
            if (current.Context.Count == 2)
            {
                TestHelper.Assert(current.Context["MyCtx"] == "hello");
                TestHelper.Assert(current.Context.ContainsKey("Direct"));
            }
            else
            {
                TestHelper.Assert(current.Context.Count == 0);
            }
        }

        public (int, string) OpReturnOut(int x, Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.Context.Count == 1);
            TestHelper.Assert(current.Context.ContainsKey("Intercepted") || current.Context.ContainsKey("Direct"));
            return (x, $"value={x}");
        }

        public void OpOneway(int x, Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.Context.Count == 1);
            TestHelper.Assert(current.Context.ContainsKey("Intercepted") || current.Context.ContainsKey("Direct"));
            TestHelper.Assert(x == 42);
        }

        public void OpMyError(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.Context.Count == 1);
            TestHelper.Assert(current.Context.ContainsKey("Intercepted") || current.Context.ContainsKey("Direct"));
            throw new MyError(42);
        }

        public void OpServiceNotFoundException(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.Context.Count == 1);
            TestHelper.Assert(current.Context.ContainsKey("Intercepted") || current.Context.ContainsKey("Direct"));
            throw new ServiceNotFoundException();
        }

        public ITestIntfPrx OpNewProxy(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.Context.Count == 1);
            TestHelper.Assert(current.Context.ContainsKey("Intercepted") || current.Context.ContainsKey("Direct"));
            return ITestIntfPrx.Factory.Create(current.Server, current.Path).Clone(
                encoding: current.Encoding);
        }

        public void Shutdown(Current current, CancellationToken cancel) => current.Server.ShutdownAsync();
    }
}
