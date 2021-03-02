// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice.Test.Objects
{
    public sealed class UnexpectedObjectExceptionTest : IService
    {
        public ValueTask<OutgoingResponseFrame> DispatchAsync(Current current, CancellationToken cancel)
        {
            var ae = new AlsoEmpty();
            var responseFrame =
                OutgoingResponseFrame.WithReturnValue(current,
                                                      compress: false,
                                                      format: default,
                                                      ae,
                                                      (OutputStream ostr, AlsoEmpty ae) => ostr.WriteClass(ae, null));
            return new(responseFrame);
        }
    }
}
