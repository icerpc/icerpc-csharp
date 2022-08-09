// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace IceRpc.Internal;

/// <summary>An <see cref="EventSource"/> implementation used to log server events.</summary>
internal sealed class ServerEventSource : EventSource
{
    internal static readonly ServerEventSource Log = new("IceRpc-Server");

    private readonly IncrementingPollingCounter _connectionsPerSecondCounter;
    private long _currentConnections;
    private readonly PollingCounter _currentConnectionsCounter;
    private long _failedConnections;
    private readonly PollingCounter _failedConnectionsCounter;
    private long _totalConnections;
    private readonly PollingCounter _totalConnectionsCounter;

    /// <summary>Creates a new instance of the <see cref="ServerEventSource"/> class with the specified name.
    /// </summary>
    /// <param name="eventSourceName">The name to apply to the event source. Must not be <c>null</c>.</param>
    internal ServerEventSource(string eventSourceName)
        : base(eventSourceName)
    {
        _connectionsPerSecondCounter = new IncrementingPollingCounter(
            "connections-per-second",
            this,
            () => Volatile.Read(ref _totalConnections))
        {
            DisplayName = "Connections Rate",
            DisplayRateTimeScale = TimeSpan.FromSeconds(1)
        };

        _currentConnectionsCounter = new PollingCounter(
            "current-connections",
            this,
            () => Volatile.Read(ref _currentConnections))
        {
            DisplayName = "Current Connections",
        };

        _failedConnectionsCounter = new PollingCounter(
            "failed-connections",
            this,
            () => Volatile.Read(ref _failedConnections))
                {
                    DisplayName = "Failed Connections",
                };

        _totalConnectionsCounter = new PollingCounter(
            "total-connections",
            this,
            () => Volatile.Read(ref _totalConnections))
        {
            DisplayName = "Total Connections",
        };
    }

    [NonEvent]
    internal void ConnectionFailure(
        Protocol protocol,
        TransportConnectionInformation connectionInformation,
        Exception exception)
    {
        Interlocked.Increment(ref _failedConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectionFailure(
                protocol.Name,
                connectionInformation.LocalNetworkAddress?.ToString(),
                connectionInformation.RemoteNetworkAddress?.ToString(),
                exception.GetType().FullName,
                exception.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStart(Protocol protocol, TransportConnectionInformation connectionInformation)
    {
        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _currentConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStart(
                protocol.Name,
                connectionInformation.LocalNetworkAddress?.ToString(),
                connectionInformation.RemoteNetworkAddress?.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStop(
        Protocol protocol,
        TransportConnectionInformation connectionInformation)
    {
        Interlocked.Decrement(ref _currentConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStop(
                protocol.Name,
                connectionInformation.LocalNetworkAddress?.ToString(),
                connectionInformation.RemoteNetworkAddress?.ToString());
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _currentConnectionsCounter.Dispose();
        _failedConnectionsCounter.Dispose();
        _connectionsPerSecondCounter.Dispose();
        _totalConnectionsCounter.Dispose();
        base.Dispose(disposing);
    }

    // Event methods sorted by eventId

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    private void ConnectionStart(string protocol, string? localNetworkAddress, string? remoteNetworkAddress) =>
        WriteEvent(1, protocol, localNetworkAddress, remoteNetworkAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ConnectionStop(
        string protocol,
        string? localNetworkAddress,
        string? remoteNetworkAddress) =>
        WriteEvent(2, protocol, localNetworkAddress, remoteNetworkAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(3, Level = EventLevel.Error)]
    private void ConnectionFailure(
        string protocol,
        string? localNetworkAddress,
        string? remoteNetworkAddress,
        string? exceptionType,
        string exceptionDetails) =>
        WriteEvent(3, protocol, localNetworkAddress, remoteNetworkAddress, exceptionType, exceptionDetails);
}
