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
    internal void ConnectFailure(ServerAddress serverAddress, Exception exception)
    {
        Interlocked.Increment(ref _totalFailedConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectFailure(serverAddress.ToString(), exception.GetType().FullName, exception.ToString());
        }
    }

    [NonEvent]
    internal void ConnectStart(ServerAddress serverAddress)
    {
        Interlocked.Increment(ref _currentQueue);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectStart(serverAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectStop(ServerAddress serverAddress)
    {
        Interlocked.Decrement(ref _currentQueue);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectStop(serverAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectSuccess(ServerAddress serverAddress, EndPoint localNetworkAddress)
    {
        Interlocked.Increment(ref _currentConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectSuccess(serverAddress.ToString(), localNetworkAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionFailure(ServerAddress serverAddress, Exception exception)
    {
        Interlocked.Increment(ref _totalFailedConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectionFailure(serverAddress.ToString(), exception.GetType().FullName, exception.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionShutdownFailure(ServerAddress serverAddress, Exception exception)
    {
        Interlocked.Increment(ref _totalFailedConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectionShutdownFailure(serverAddress.ToString(), exception.GetType().FullName, exception.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStart(ServerAddress serverAddress)
    {
        Interlocked.Increment(ref _totalConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStart(serverAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStop(ServerAddress serverAddress)
    {
        Interlocked.Decrement(ref _currentConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStop(serverAddress.ToString());
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
    private void ConnectionStart(string serverAddress) =>
        WriteEvent(1, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ConnectionStop(string serverAddress) =>
        WriteEvent(2, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(3, Level = EventLevel.Error)]
    private void ConnectionFailure(string serverAddress, string? exceptionType, string exceptionDetails) =>
        WriteEvent(3, serverAddress, exceptionType, exceptionDetails);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(4, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    private void ConnectStart(string serverAddress) =>
        WriteEvent(4, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(5, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ConnectStop(string serverAddress) =>
        WriteEvent(5, serverAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(6, Level = EventLevel.Error)]
    private void ConnectFailure(string serverAddress, string? exceptionType, string exceptionDetails) =>
        WriteEvent(6, serverAddress, exceptionType, exceptionDetails);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(7, Level = EventLevel.Informational)]
    private void ConnectSuccess(string serverAddress, string? localNetworkAddress) =>
        WriteEvent(7, serverAddress, localNetworkAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(8, Level = EventLevel.Error)]
    private void ConnectionShutdownFailure(string serverAddress, string? exceptionType, string exceptionDetails) =>
        WriteEvent(8, serverAddress, exceptionType, exceptionDetails);
}
