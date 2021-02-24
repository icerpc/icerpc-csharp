// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;

namespace ZeroC.Ice.Test.Facets
{
    public sealed class H : IH
    {
        public string CallG(Current current, CancellationToken cancel) => "G";

        public string CallH(Current current, CancellationToken cancel) => "H";

        public void Shutdown(Current current, CancellationToken cancel) => current.Server.ShutdownAsync();
    }
}
