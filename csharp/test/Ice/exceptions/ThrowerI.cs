// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Exceptions
{
    public sealed class Forwarder : IService
    {
        private IServicePrx _target;

        ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(
            Current current,
            CancellationToken cancel)
            => _target.ForwardAsync(current.IncomingRequestFrame, current.IsOneway, cancel: cancel);

        internal Forwarder(IServicePrx target) => _target = target;
    }

    public sealed class Thrower : IThrower
    {
        // 20KB is over the configured 10KB message size max.
        public ReadOnlyMemory<byte> SendAndReceive(byte[] seq, Current current, CancellationToken cancel) =>
            new byte[1024 * 20];

        public void Shutdown(Current current, CancellationToken cancel) =>
            current.Server.ShutdownAsync();

        public bool SupportsAssertException(Current current, CancellationToken cancel) => false;

        public void ThrowAasA(int a, Current current, CancellationToken cancel) => throw new A(a);

        public void ThrowAorDasAorD(int a, Current current, CancellationToken cancel)
        {
            if (a > 0)
            {
                throw new A(a);
            }
            else
            {
                throw new D(a);
            }
        }

        public void ThrowBasA(int a, int b, Current current, CancellationToken cancel) => ThrowBasB(a, b, current, cancel);

        public void ThrowBasB(int a, int b, Current current, CancellationToken cancel) => throw new B(a, b);

        public void ThrowCasA(int a, int b, int c, Current current, CancellationToken cancel) =>
            ThrowCasC(a, b, c, current, cancel);

        public void ThrowCasB(int a, int b, int c, Current current, CancellationToken cancel) =>
            ThrowCasC(a, b, c, current, cancel);

        public void ThrowCasC(int a, int b, int c, Current current, CancellationToken cancel) => throw new C(a, b, c);

        public void ThrowLocalException(Current current, CancellationToken cancel) =>
            throw new ConnectionClosedException();

        public void ThrowNonIceException(Current current, CancellationToken cancel) => throw new Exception();

        public void ThrowAssertException(Current current, CancellationToken cancel) => TestHelper.Assert(false);

        public void ThrowLocalExceptionIdempotent(Current current, CancellationToken cancel) =>
            throw new ConnectionClosedException();

        public void ThrowAfterResponse(Current current, CancellationToken cancel)
        {
            // Only relevant for AMD.
        }

        // Only relevant for AMD.
        public void ThrowAfterException(Current current, CancellationToken cancel) => throw new A();

        public void ThrowAConvertedToUnhandled(Current current, CancellationToken cancel) =>
            throw new A() { ConvertToUnhandled = true };
    }
}
