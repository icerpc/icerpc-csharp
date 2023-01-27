// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IceRpc.Internal;

/// <summary>A helper class used to report client and server connection metrics.</summary>
internal sealed class Metrics : IDisposable
{
    internal static readonly Metrics ClientMetrics = new("IceRpc.Client");
    internal static readonly Metrics ServerMetrics = new("IceRpc.Server");

    // The number of active connections.
    private long _activeConnections;

    private readonly Meter _meter;

    // The number of connections that are being connected.
    private long _pendingConnections;

    // The number of connection that have been created.
    private long _totalConnections;

    // The number of connections that start connecting and failed to connect, or connected successfully but
    // later terminate due to a failure.
    private long _totalFailedConnections;

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();

    internal Metrics(string meterName)
    {
        _meter = new Meter(meterName);

        _meter.CreateObservableUpDownCounter(
            "active-connections",
            () => Volatile.Read(ref _activeConnections),
            "Connections",
            "Active Connections");

        _meter.CreateObservableUpDownCounter(
            "pending-connections",
            () => Volatile.Read(ref _pendingConnections),
            "Connections",
            "Pending Connections");

        _meter.CreateObservableCounter(
            "total-connections",
            () => Volatile.Read(ref _totalConnections),
            "Connections",
            "Total Connections");

        _meter.CreateObservableCounter(
            "total-failed-connections",
            () => Volatile.Read(ref _totalFailedConnections),
            "Connections",
            "Total Failed Connections");
    }

    internal void ConnectStart()
    {
        Debug.Assert(_totalConnections >= 0);
        Debug.Assert(_pendingConnections >= 0);
        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _pendingConnections);
    }

    internal void ConnectStop()
    {
        Debug.Assert(_pendingConnections > 0);
        Interlocked.Decrement(ref _pendingConnections);
    }

    internal void ConnectSuccess()
    {
        Debug.Assert(_activeConnections >= 0);
        Interlocked.Increment(ref _activeConnections);
    }

    internal void ConnectionFailure()
    {
        Debug.Assert(_totalFailedConnections >= 0);
        Debug.Assert(_totalFailedConnections < _totalConnections);
        Interlocked.Increment(ref _totalFailedConnections);
    }

    internal void ConnectionDisconnected()
    {
        Debug.Assert(_activeConnections > 0);
        Interlocked.Decrement(ref _activeConnections);
    }
}
