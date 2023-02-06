// Copyright (c) ZeroC, Inc.

using System.Diagnostics.Metrics;

namespace IceRpc.Metrics.Internal;

/// <summary>A helper class used to report invocation metrics.</summary>
internal class InvocationMetrics : IDisposable
{
    internal static readonly InvocationMetrics Instance = new("IceRpc.Invocation");

    private readonly Meter _meter;
    private readonly Counter<long> _canceledRequests;
    private readonly UpDownCounter<long> _currentRequests;
    private readonly Counter<long> _failedRequests;
    private readonly Counter<long> _totalRequests;

    public void Dispose() => _meter.Dispose();

    internal InvocationMetrics(string name)
    {
        _meter = new Meter(name);
        _canceledRequests = _meter.CreateCounter<long>("canceled-requests", "Requests", "Canceled Requests");
        _currentRequests = _meter.CreateUpDownCounter<long>("current-requests", "Requests", "Current Requests");
        _failedRequests = _meter.CreateCounter<long>("failed-requests", "Requests", "Failed Requests");
        _totalRequests = _meter.CreateCounter<long>("total-requests", "Requests", "Total Requests");
    }

    internal void RequestCancel() => _canceledRequests.Add(1);

    internal void RequestFailure() => _failedRequests.Add(1);

    internal void RequestStart()
    {
        _currentRequests.Add(1);
        _totalRequests.Add(1);
    }

    internal void RequestStop() => _currentRequests.Add(-1);
}
