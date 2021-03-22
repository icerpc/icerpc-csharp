// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using IceRpc.Interop;
using IceRpc.Test;

namespace IceRpc.Test.Echo
{
    public class BlobjectI : IService
    {
        public ValueTask<OutgoingResponseFrame> DispatchAsync(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.Connection != null);
            IServicePrx proxy = IServicePrx.Factory.Create(current.Connection, current.Path);

            string facet = current.IncomingRequestFrame.GetFacet();
            if (facet.Length > 0)
            {
                proxy = proxy.WithFacet<IServicePrx>(facet);
            }

            return proxy.ForwardAsync(current.IncomingRequestFrame, current.IsOneway, cancel: cancel);
        }
    }
}
