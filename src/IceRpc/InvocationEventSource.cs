// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace IceRpc
{
    /// <summary>An <see cref="EventSource"/> implementation used to log invocation dispatch events.</summary>
    public sealed class InvocationEventSource : EventSource
    {
        /// <summary>The default <c>InvocationEventSource</c> used by <see cref="MetricsInterceptor"/>.
        /// </summary>
        public static readonly InvocationEventSource Log = new("IceRpc.Invocation");

        private readonly PollingCounter _canceledRequestsCounter;
        private long _canceledRequests;
        private readonly PollingCounter _currentRequestsCounter;
        private long _currentRequests;
        private readonly PollingCounter _failedRequestsCounter;
        private long _failedRequests;
        private readonly IncrementingPollingCounter _requestsPerSecondCounter;
        private readonly PollingCounter _totalRequestsCounter;
        private long _totalRequests;

        /// <summary>Creates a new instance of the <see cref="InvocationEventSource"/> class with the specified name.
        /// </summary>
        /// <param name="eventSourceName">The name to apply to the event source. Must not be <c>null</c>.</param>
        public InvocationEventSource(string eventSourceName)
            : base(eventSourceName)
        {
            _canceledRequestsCounter = new PollingCounter(
                "canceled-requests",
                this,
                () => Volatile.Read(ref _canceledRequests))
            {
                DisplayName = "Canceled Requests",
            };

            _currentRequestsCounter = new PollingCounter(
                "current-requests",
                this,
                () => Volatile.Read(ref _currentRequests))
            {
                DisplayName = "Current Requests",
            };

            _failedRequestsCounter = new PollingCounter(
                "failed-requests",
                this,
                () => Volatile.Read(ref _failedRequests))
            {
                DisplayName = "Failed Requests",
            };

            _requestsPerSecondCounter = new IncrementingPollingCounter(
                "requests-per-second",
                this,
                () => Volatile.Read(ref _totalRequests))
            {
                DisplayName = "Request Rate",
                DisplayUnits = "req/s",
                DisplayRateTimeScale = TimeSpan.FromSeconds(1)
            };

            _totalRequestsCounter = new PollingCounter(
                "total-requests",
                this,
                () => Volatile.Read(ref _totalRequests))
            {
                DisplayName = "Total Requests",
            };
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            _canceledRequestsCounter?.Dispose();
            _currentRequestsCounter?.Dispose();
            _failedRequestsCounter?.Dispose();
            _requestsPerSecondCounter?.Dispose();
            _totalRequestsCounter?.Dispose();
            base.Dispose(disposing);
        }

        [NonEvent]
        internal void RequestStart(OutgoingRequest request)
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _currentRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestStart(request.Path, request.Operation);
            }
        }

        [NonEvent]
        internal void RequestStop(OutgoingRequest request)
        {
            Interlocked.Decrement(ref _currentRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestStop(request.Path, request.Operation);
            }
        }

        [NonEvent]
        internal void RequestCanceled(OutgoingRequest request)
        {
            Interlocked.Increment(ref _canceledRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestCanceled(request.Path, request.Operation);
            }
        }

        [NonEvent]
        internal void RequestFailed(OutgoingRequest request, Exception exception) =>
            RequestFailed(request, exception?.GetType().ToString() ?? "");

        [NonEvent]
        internal void RequestFailed(OutgoingRequest request, string exception)
        {
            Interlocked.Increment(ref _failedRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestFailed(request.Path, request.Operation, exception);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(3, Level = EventLevel.Informational)]
        private void RequestCanceled(string path, string operation) =>
            WriteEvent(3, path, operation);

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(4, Level = EventLevel.Informational)]
        private void RequestFailed(string path, string operation, string exception) =>
            WriteEvent(4, path, operation, exception);

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
        private void RequestStart(string path, string operation) =>
            WriteEvent(1, path, operation);

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
        private void RequestStop(string path, string operation) =>
            WriteEvent(2, path, operation);
    }
}
