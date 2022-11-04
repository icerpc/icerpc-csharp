// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Metrics;

namespace IceRpc.Internal;

/// <summary>A helper class used to report client metrics.</summary>
internal sealed class ClientMetrics : IDisposable
{
    internal static readonly ClientMetrics Instance = new("IceRpc.Client");

    // The number of connections that were created and are being connected
    private long _currentQueue;

    // The number of active connections
    private long _currentConnections;

    private readonly Meter _meter;

    // The number of connection that have been created and connected.
    private long _totalConnections;

    // The number of connections that failed after creation.
    private long _totalFailedConnections;

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();

    /// <summary>Creates a new instance of the <see cref="ClientMetrics" /> class with the specified name.
    /// </summary>
    /// <param name="meterName">The name to apply to the meter.</param>
    internal ClientMetrics(string meterName)
    {
        _meter = new Meter(meterName);

        _meter.CreateObservableUpDownCounter(
            "current-queue",
            () => Volatile.Read(ref _currentQueue),
            "Connections",
            "Current Queue");

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

    internal void ConnectStart() => Interlocked.Increment(ref _currentQueue);

    internal void ConnectStop() => Interlocked.Decrement(ref _currentQueue);

    internal void ConnectSuccess() => Interlocked.Increment(ref _currentConnections);

    internal void ConnectionFailure() => Interlocked.Increment(ref _totalFailedConnections);

    internal void ConnectionStart() => Interlocked.Increment(ref _totalConnections);

    internal void ConnectionStop() => Interlocked.Decrement(ref _currentConnections);
}
