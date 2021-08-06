// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A middleware that publishes dispatch metrics using a dispatch event source.</summary>
    public class MetricsMiddleware : IDispatcher
    {
        private readonly DispatchEventSource _eventSource;
        private readonly IDispatcher _next;

        /// <summary>Constructs a metrics middleware.</summary>
        /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
        /// <param name="eventSource">The dispatch event source used to publish the metrics events.</param>
        public MetricsMiddleware(IDispatcher next, DispatchEventSource eventSource)
        {
            _next = next;
            _eventSource = eventSource;
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            _eventSource.RequestStart(request);
            try
            {
                OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
                if (response.ResultType == ResultType.Failure)
                {
                    _eventSource.RequestFailed(request, "IceRpc.RemoteException");
                }
                return response;
            }
            catch (OperationCanceledException)
            {
                _eventSource.RequestCanceled(request);
                throw;
            }
            catch (Exception ex)
            {
                _eventSource.RequestFailed(request, ex);
                throw;
            }
            finally
            {
                _eventSource.RequestStop(request);
            }
        }
    }
}