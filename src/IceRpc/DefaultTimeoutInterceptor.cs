// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>An interceptor that sets the invocation timeout, the interceptor sets the
    /// <see cref="OutgoingRequest.Deadline"/> and creates a cancellation token that enforces it. If
    /// <see cref="Invocation.Deadline"/> or <see cref="Invocation.Timeout"/> are set to a value other
    /// than <see cref="DateTime.MaxValue"/> or <see cref="Timeout.InfiniteTimeSpan"/> respectively,
    /// the invocation settings prevail and this interceptor does nothing.</summary>
    public class TimeoutInterceptor : IInvoker
    {
        private readonly IInvoker _next;
        private readonly TimeSpan _timeout;

        /// <summary>Constructs a default timeout interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="timeout">The timeout for the invocation.</param>
        public TimeoutInterceptor(IInvoker next, TimeSpan timeout)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentException($"{nameof(timeout)} cannot be infinite", nameof(timeout));
            }
            else if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentException($"{nameof(timeout)} must be greater than 0", nameof(timeout));
            }

            _next = next;
            _timeout = timeout;
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // If the Invocation sets a timeout or deadline (other than max value), the timeout/deadline set
            // by the Invocation prevails and the interceptor does nothing.
            if (request.Deadline != DateTime.MaxValue)
            {
                return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
            else
            {
                using var timeoutTokenSource = new CancellationTokenSource(_timeout);
                using CancellationTokenSource linkedTokenSource = cancel.CanBeCanceled ?
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancel, timeoutTokenSource.Token) : timeoutTokenSource;
                request.Deadline = DateTime.UtcNow + _timeout;
                return await _next.InvokeAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
            }
        }
    }
}
