// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Echo
{
    public class BlobjectI : IService
    {
        public ValueTask<OutgoingResponseFrame> DispatchAsync(
            IncomingRequestFrame request,
            Current current,
            CancellationToken cancel)
        {
            TestHelper.Assert(current.Connection != null);
            IServicePrx proxy = IServicePrx.Factory.Create(current.Connection, current.Path, current.Facet);
            return proxy.ForwardAsync(request, current.IsOneway, cancel: cancel);
        }
    }
}
