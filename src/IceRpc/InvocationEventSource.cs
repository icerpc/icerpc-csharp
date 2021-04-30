// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IceRpc
{
    /// <summary>An <see cref="EventSource"/> implementation used to log request dispatch events. Instances of this</summary>
    public sealed class InvocationEventSource : EventSource
    {
        public static readonly InvocationEventSource Log = new InvocationEventSource("IceRpc.Invocation");
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

        /// <summary>Creates a new instance of the <see cref="InvocationEventSource"/> class with the specified name.
        /// </summary>
        /// <param name="eventSourceName">The name to apply to the event source. Must not be <c>null</c>.</param>
        public InvocationEventSource(string eventSourceName)
            : base(eventSourceName)
        {
        }

        [NonEvent]
        public void RequestStart(OutgoingRequest request)
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _currentRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestStart(request.Path, request.Operation);
            }
        }

        [NonEvent]
        public void RequestStop(OutgoingRequest request)
        {
            Interlocked.Decrement(ref _currentRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestStop(request.Path, request.Operation);
            }
        }

        [NonEvent]
        public void RequestCanceled(OutgoingRequest request)
        {
            Interlocked.Increment(ref _canceledRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestCanceled(request.Path, request.Operation);
            }
        }

        [NonEvent]
        public void RequestFailed(OutgoingRequest request, Exception exception) =>
            RequestFailed(request, exception?.GetType().FullName ?? "");

        [NonEvent]
        public void RequestFailed(OutgoingRequest request, string exception)
        {
            Interlocked.Increment(ref _failedRequests);
            if (IsEnabled(EventLevel.Informational, EventKeywords.None))
            {
                RequestFailed(request.Path, request.Operation, exception);
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
                    DisplayUnits = "req/s",
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Event(3, Level = EventLevel.Informational)]
        public void RequestCanceled(string path, string operation) =>
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

    public static partial class Interceptor
    {
        /// <summary>Creates an interceptor that publishes invocation metrics using an <see cref="InvocationEventSource"/>.
        /// </summary>
        /// <param name="eventSource">The event source used to publish the metrics events.</param>
        public static Func<IInvoker, IInvoker> CreateMetricsPublisher(InvocationEventSource eventSource) =>
            next => new InlineInvoker(
                async (request, cancel) =>
                {
                    eventSource.RequestStart(request);
                    try
                    {
                        var response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                        if (response.ResultType == ResultType.Failure)
                        {
                            eventSource.RequestFailed(request, "IceRpc.RemoteException");
                        }
                        return response;
                    }
                    catch (OperationCanceledException)
                    {
                        eventSource.RequestCanceled(request);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        eventSource.RequestFailed(request, ex);
                        throw;
                    }
                    finally
                    {
                        eventSource.RequestStop(request);
                    }
                });

        /// <summary>A interceptor that publishes invocation metrics, using the default
        /// <see cref="InvocationEventSource.Log"/> instance.</summary>
        public static Func<IInvoker, IInvoker> MetricsPublisher { get; } =
            CreateMetricsPublisher(InvocationEventSource.Log);
    }
}
