// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;

namespace IceRpc.Test.Facets
{
    public sealed class E : IE
    {
        public string CallE(Current current, CancellationToken cancel) => "E";
    }
}
