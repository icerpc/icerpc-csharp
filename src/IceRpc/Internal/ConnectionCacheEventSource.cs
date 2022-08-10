// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Tracing;
using System.Net;
using System.Runtime.CompilerServices;

namespace IceRpc.Internal;

/// <summary>An <see cref="EventSource"/> implementation used to log connection cache events.</summary>
internal sealed class ConnectionCacheEventSource : EventSource
{
    internal static readonly ConnectionCacheEventSource Log = new("IceRpc-ConnectionCache");

    // The number of connections that were created and are being connected
    private long _currentQueue;
    private readonly PollingCounter _currentQueueCounter;

    // The number of active connections
    private long _currentConnections;
    private readonly PollingCounter _currentConnectionsCounter;

    // Tracks the connection rate
    private readonly IncrementingPollingCounter _connectionsPerSecondCounter;

    // The number of connection that have been accepted and connected.
    private long _totalConnections;
    private readonly PollingCounter _totalConnectionsCounter;

    // The number of connections that were accepted but failed to connect plus lost connections.
    private long _totalFailedConnections;
    private readonly PollingCounter _totalFailedConnectionsCounter;

    /// <summary>Creates a new instance of the <see cref="ConnectionCacheEventSource"/> class with the specified name.
    /// </summary>
    /// <param name="eventSourceName">The name to apply to the event source.</param>
    internal ConnectionCacheEventSource(string eventSourceName)
        : base(eventSourceName)
    {
        _currentQueueCounter = new PollingCounter(
            "current-queue",
            this,
            () => Volatile.Read(ref _currentQueue))
        {
            DisplayName = "Current Queue",
        };

        _currentConnectionsCounter = new PollingCounter(
            "current-connections",
            this,
            () => Volatile.Read(ref _currentConnections))
        {
            DisplayName = "Current Connections",
        };

        _connectionsPerSecondCounter = new IncrementingPollingCounter(
            "connections-per-second",
            this,
            () => Volatile.Read(ref _totalConnections))
        {
            DisplayName = "Connections Rate",
            DisplayRateTimeScale = TimeSpan.FromSeconds(1)
        };

        _totalConnectionsCounter = new PollingCounter(
            "total-connections",
            this,
            () => Volatile.Read(ref _totalConnections))
        {
            DisplayName = "Total Connections",
        };

        _totalFailedConnectionsCounter = new PollingCounter(
            "total-failed-connections",
            this,
            () => Volatile.Read(ref _totalFailedConnections))
                {
                    DisplayName = "Total Failed Connections",
                };
    }

    [NonEvent]
    internal void ConnectFailure(string name, ServerAddress serverAddress, Exception exception)
    {
        Interlocked.Increment(ref _totalFailedConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectFailure(
                name,
                serverAddress.ToString(),
                exception.GetType().FullName,
                exception.ToString());
        }
    }

    [NonEvent]
    internal void ConnectStart(string name, ServerAddress serverAddress)
    {
        Interlocked.Increment(ref _currentQueue);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectStart(name, serverAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectStop(string name, ServerAddress serverAddress)
    {
        Interlocked.Decrement(ref _currentQueue);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectStop(name, serverAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectSuccess(string name, ServerAddress serverAddress, EndPoint localNetworkAddress)
    {
        Interlocked.Increment(ref _currentConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectSuccess(name, serverAddress.ToString(), localNetworkAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStart(string name, ServerAddress serverAddress)
    {
        Interlocked.Increment(ref _totalConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStart(name, serverAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStop(string name, ServerAddress serverAddress)
    {
        Interlocked.Increment(ref _currentConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStop(name, serverAddress.ToString());
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _currentConnectionsCounter.Dispose();
        _currentQueueCounter.Dispose();
        _connectionsPerSecondCounter.Dispose();
        _totalConnectionsCounter.Dispose();
        _totalFailedConnectionsCounter.Dispose();
        base.Dispose(disposing);
    }

    // Event methods sorted by eventId

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    private void ConnectionStart(string name, string serverAddress) =>
        WriteEvent(1, name, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ConnectionStop(string name, string serverAddress) =>
        WriteEvent(2, name, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(3, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    private void ConnectStart(string name, string serverAddress) =>
        WriteEvent(3, name, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(4, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ConnectStop(string name, string serverAddress) =>
        WriteEvent(4, name, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(5, Level = EventLevel.Error)]
    private void ConnectFailure(
        string name,
        string serverAddress,
        string? exceptionType,
        string exceptionDetails) =>
        WriteEvent(
            5,
            name,
            serverAddress,
            exceptionType,
            exceptionDetails);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(6, Level = EventLevel.Informational)]
    private void ConnectSuccess(string name, string serverAddress, string? localNetworkAddress) =>
        WriteEvent(6, name, serverAddress, localNetworkAddress);
}
