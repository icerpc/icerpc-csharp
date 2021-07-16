// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>An interceptor that sets the invocation timeout.</summary>
        /// <param name="timeout">The timeout for the invocation.</param>
        /// <returns>The CustomTiemout interceptor.</returns>
        public static Func<IInvoker, IInvoker> CustomTiemout(TimeSpan timeout)
        {
            if (timeout != Timeout.InfiniteTimeSpan && timeout < TimeSpan.Zero)
            {
                throw new ArgumentException($"{nameof(timeout)} must be greater than 0", nameof(timeout));
            }

            return next => new InlineInvoker(async (request, cancel) =>
                {
                    if (timeout == Timeout.InfiniteTimeSpan)
                    {
                        return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        using var timeoutTokenSource = new CancellationTokenSource(timeout);
                        using var linkedTokenSource = cancel.CanBeCanceled ? 
                            CancellationTokenSource.CreateLinkedTokenSource(
                                cancel, timeoutTokenSource.Token) : timeoutTokenSource;
                        request.Deadline = DateTime.UtcNow + timeout;
                        return await next.InvokeAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
                    }
                });
        }
    }
}
