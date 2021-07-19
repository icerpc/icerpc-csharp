// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>An interceptor that sets the invocation timeout, the interceptor sets the
        /// <see cref="OutgoingRequest.Deadline"/> and creates a cancellation token that enforces it. If
        /// <see cref="Invocation.Deadline"/> or <see cref="Invocation.Timeout"/> are set to a value other
        /// than <see cref="DateTime.MaxValue"/> or <see cref="Timeout.InfiniteTimeSpan"/> respectively,
        /// the invocation settings prevail and this interceptor does nothing.</summary>
        /// <param name="timeout">The timeout for the invocation.</param>
        /// <returns>The timeout interceptor.</returns>
        /// <see cref="Invocation.Timeout"/>
        public static Func<IInvoker, IInvoker> Timeout(TimeSpan timeout)
        {
            if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentException($"{nameof(timeout)} cannot be infinite", nameof(timeout));
            }
            else if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentException($"{nameof(timeout)} must be greater than 0", nameof(timeout));
            }

            return next => new InlineInvoker(async (request, cancel) =>
                {
                    // If the Invocation sets a timeout or deadline (other than max value), the timeout/deadline set
                    // by the Invocation prevails and the interceptor does nothing.
                    if (request.Deadline != DateTime.MaxValue)
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
