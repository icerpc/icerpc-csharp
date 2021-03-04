// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;

namespace IceRpc.Test.Inheritance
{
    public sealed class A : MA.IA
    {
        public MA.IAPrx? Iaop(MA.IAPrx? p, Current current, CancellationToken cancel) => p;
    }
}
