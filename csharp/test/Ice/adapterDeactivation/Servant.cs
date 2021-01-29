// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.AdapterDeactivation
{
    public sealed class Servant : IObject
    {
        public ValueTask<OutgoingResponseFrame> DispatchAsync(
            IncomingRequestFrame request,
            Current current,
            CancellationToken cancel)
        {
            TestHelper.Assert(current.Identity.Category.Length == 0);
            TestHelper.Assert(current.Identity.Name == "test");
            IObject servant = new TestIntf();
            return servant.DispatchAsync(request, current, cancel);
        }
    }
}
