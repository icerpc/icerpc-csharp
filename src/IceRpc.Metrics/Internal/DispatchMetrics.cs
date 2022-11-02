// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Metrics;

namespace IceRpc.Metrics.Internal;

/// <summary>A helper class used to report dispatch related <see cref="Metrics"/>.</summary>
internal static class DispatchMetrics
{
    private static readonly Meter _meter = new("IceRpc.Dispatch");

    private static readonly Counter<long> _canceledRequests = _meter.CreateCounter<long>(
        "canceled-requests",
        "Requests",
        "Canceled Requests");

    private static readonly UpDownCounter<long> _currentRequests = _meter.CreateUpDownCounter<long>(
        "current-requests",
        "Requests",
        "Current Requests");

    private static readonly Counter<long> _failedRequests = _meter.CreateCounter<long>(
        "failed-requests",
        "Requests",
        "Failed Requests");

    private static readonly Counter<long> _totalRequests = _meter.CreateCounter<long>(
        "total-requests",
        "Requests",
        "Total Requests");

    internal static void RequestCancel() => _canceledRequests.Add(1);

    internal static void RequestFailure() => _failedRequests.Add(1);

    internal static void RequestStart()
    {
        _currentRequests.Add(1);
        _totalRequests.Add(1);
    }

    internal static void RequestStop() => _currentRequests.Add(-1);
}
