// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IceRpc.Internal;

/// <summary>A helper class used to report client and server connection metrics.</summary>
internal sealed class Metrics : IDisposable
{
    internal static readonly Metrics ClientMetrics = new("IceRpc.Client");
    internal static readonly Metrics ServerMetrics = new("IceRpc.Server");

    // The number of active (accepted and connected) connections.
    private long _currentConnections;

    private readonly Meter _meter;

    // The number of connections that were accepted and are being connected.
    private long _pendingConnections;

    // The number of connection that have been accepted and connected.
    private long _totalConnections;

    // The number of connections that were accepted and failed later on.
    private long _totalFailedConnections;

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();

    internal Metrics(string meterName)
    {
        _meter = new Meter(meterName);

        _meter.CreateObservableUpDownCounter(
            "current-connections",
            () => Volatile.Read(ref _currentConnections),
            "Connections",
            "Current Connections");

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
        Debug.Assert(_pendingConnections >= 0);
        Interlocked.Increment(ref _pendingConnections);
    }

    internal void ConnectStop()
    {
        Debug.Assert(_pendingConnections > 0);
        Interlocked.Decrement(ref _pendingConnections);
    }

    internal void ConnectSuccess()
    {
        Debug.Assert(_currentConnections >= 0);
        Interlocked.Increment(ref _currentConnections);
    }

    internal void ConnectionFailure()
    {
        Debug.Assert(_totalFailedConnections >= 0);
        Interlocked.Increment(ref _totalFailedConnections);
    }

    internal void ConnectionStart()
    {
        Debug.Assert(_totalConnections >= 0);
        Interlocked.Increment(ref _totalConnections);
    }

    internal void ConnectionStop()
    {
        Debug.Assert(_currentConnections > 0);
        Interlocked.Decrement(ref _currentConnections);
    }
}
