// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;

namespace IceRpc.Test.Facets
{
    public sealed class A : IA
    {
        public string CallA(Current current, CancellationToken cancel) => "A";
    }
}
