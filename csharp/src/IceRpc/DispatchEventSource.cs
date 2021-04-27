// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IceRpc
{
    [EventSource(Name = "IceRpc-Dispatch")]
    internal sealed class DispatchEventSource : EventSource
    {
        internal static readonly DispatchEventSource Log = new DispatchEventSource();
#pragma warning disable IDE0052 // Remove unread private members, IDE is wrong here counters are used in OnEventCommand
        private PollingCounter? _canceledRequestsCounter;
        private long _canceledRequests;
        private PollingCounter? _currentRequestsCounter;
        private long _currentRequests;
        private PollingCounter? _failedRequestsCounter;
        private long _failedRequests;
        private IncrementingPollingCounter? _requestsPerSecondCounter;
        private PollingCounter? _totalRequestsCounter;
        private long _totalRequests;
#pragma warning restore IDE0052 // Remove unread private members

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(1, Level = EventLevel.Informational)]
        public void RequestStart(string path, string operation)
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _currentRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(1, path, operation);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(2, Level = EventLevel.Informational)]
        public void RequestStop(string path, string operation)
        {
            Interlocked.Decrement(ref _currentRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(2, path, operation);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(3, Level = EventLevel.Informational)]
        public void RequestCanceled(string path, string operation)
        {
            Interlocked.Increment(ref _canceledRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                WriteEvent(3, path, operation);
            }
        }

        [NonEvent]
        public void RequestFailed(string path, string operation, Exception? exception)
        {
            Interlocked.Increment(ref _failedRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestFailed(path, operation, exception?.ToString() ?? "");
            }
        }

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            if (command.Command == EventCommand.Enable)
            {
                // This is the convention for initializing counters in the RuntimeEventSource (lazily on the first enable command).
                // They aren't disabled afterwards...

                _canceledRequestsCounter ??= new PollingCounter(
                    "canceled-requests",
                    this,
                    () => Volatile.Read(ref _canceledRequests))
                {
                    DisplayName = "Canceled Requests",
                };

                _currentRequestsCounter ??= new PollingCounter(
                    "current-requests",
                    this,
                    () => Volatile.Read(ref _currentRequests))
                {
                    DisplayName = "Current Requests",
                };

                _failedRequestsCounter ??= new PollingCounter(
                    "failed-requests",
                    this,
                    () => Volatile.Read(ref _failedRequests))
                {
                    DisplayName = "Failed Requests",
                };

                _requestsPerSecondCounter ??= new IncrementingPollingCounter(
                    "requests-per-second",
                    this,
                    () => Volatile.Read(ref _totalRequests))
                {
                    DisplayName = "Request Rate",
                    DisplayRateTimeScale = TimeSpan.FromSeconds(1)
                };

                _totalRequestsCounter ??= new PollingCounter(
                    "total-requests",
                    this,
                    () => Volatile.Read(ref _totalRequests))
                {
                    DisplayName = "Total Requests",
                };
            }
        }

        // Used for testing
        internal DispatchEventSource(string eventSourceName)
            : base(eventSourceName)
        {
        }

        [NonEvent]
        internal void ResetCounters()
        {
            _canceledRequests = 0;
            _currentRequests = 0;
            _failedRequests = 0;
            _totalRequests = 0;
        }

        private DispatchEventSource() : base("IceRpc.Dispatch")
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(4, Level = EventLevel.Informational)]
        private void RequestFailed(string path, string operation, string exception) =>
            WriteEvent(4, path, operation, exception);
    }
}
