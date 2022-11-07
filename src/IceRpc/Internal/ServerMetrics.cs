// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Metrics;

namespace IceRpc.Internal;

/// <summary>A helper class used to report server metrics.</summary>
internal sealed class ServerMetrics : IDisposable
{
    internal static readonly ServerMetrics Instance = new("IceRpc.Server");

    // The number of connections that were accepted and are being connected.
    private long _pendingConnections;

    // The number of active (accepted and connected) connections.
    private long _currentConnections;

    // The number of connection that have been accepted and connected.
    private long _totalConnections;

    // The number of connections that were accepted and failed later on.
    private long _totalFailedConnections;

    private readonly Meter _meter;

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();

    internal ServerMetrics(string meterName)
    {
        _meter = new Meter(meterName);

        _meter.CreateObservableUpDownCounter(
            "pending-connections",
            () => Volatile.Read(ref _pendingConnections),
            "Connections",
            "Pending Connections");

        _meter.CreateObservableUpDownCounter(
            "current-connections",
            () => Volatile.Read(ref _currentConnections),
            "Connections",
            "Current Connections");

        _meter.CreateObservableCounter(
            "total-connections",
            () => Volatile.Read(ref _totalConnections),
            "Connections",
            "Total Connections");

        _meter.CreateObservableCounter(
            "total-failed-connections",
            () => Volatile.Read(ref _totalConnections),
            "Connections",
            "Total Failed Connections");
    }

    internal void ConnectStart() => Interlocked.Increment(ref _pendingConnections);

    internal void ConnectStop() => Interlocked.Decrement(ref _pendingConnections);

    internal void ConnectSuccess() => Interlocked.Increment(ref _currentConnections);

    internal void ConnectionFailure() => Interlocked.Increment(ref _totalFailedConnections);

    internal void ConnectionStart() => Interlocked.Increment(ref _totalConnections);

    internal void ConnectionStop() => Interlocked.Decrement(ref _currentConnections);
}
