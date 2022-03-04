﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>The timeout interceptor adds and enforces a timeout for requests with no deadline set.</summary>
    /// <remarks>This interceptor sets the request's deadline. As a result, if you insert more than one timeout
    /// interceptor in an invocation pipeline, only the first one has any effect.</remarks>
    public class TimeoutInterceptor : IInvoker
    {
        private readonly IInvoker _next;
        private readonly TimeSpan _timeout;

        /// <summary>Constructs a timeout interceptor.</summary>
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
            // If the deadline field is already set, we don't do anything
            if (request.Fields.ContainsKey((int)FieldKey.Deadline))
            {
                return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
            else
            {
                using var timeoutTokenSource = new CancellationTokenSource(_timeout);
                using CancellationTokenSource linkedTokenSource = cancel.CanBeCanceled ?
                    CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutTokenSource.Token) :
                    timeoutTokenSource;

                // We compute the deadline immediately
                long deadline = (long)(DateTime.UtcNow + _timeout - DateTime.UnixEpoch).TotalMilliseconds;
                Debug.Assert(deadline > 0);

                request.Fields = request.Fields.With(
                    (int)FieldKey.Deadline,
                    (ref SliceEncoder encoder) => encoder.EncodeVarLong(deadline));

                return await _next.InvokeAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
            }
        }
    }
}
