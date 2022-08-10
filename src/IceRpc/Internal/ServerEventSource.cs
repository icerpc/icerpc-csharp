// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.Tracing;
using System.Net;
using System.Runtime.CompilerServices;

namespace IceRpc.Internal;

/// <summary>An <see cref="EventSource"/> implementation used to log server events.</summary>
internal sealed class ServerEventSource : EventSource
{
    internal static readonly ServerEventSource Log = new("IceRpc-Server");

    // The number of connections that were accepted and are being connected
    private long _currentBacklog;
    private readonly PollingCounter _currentBacklogCounter;

    // The number of connections that were accepted and connected and are not lost or shutdown (“active” connections).
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

    /// <summary>Creates a new instance of the <see cref="ServerEventSource"/> class with the specified name.
    /// </summary>
    /// <param name="eventSourceName">The name to apply to the event source.</param>
    internal ServerEventSource(string eventSourceName)
        : base(eventSourceName)
    {
        _currentBacklogCounter = new PollingCounter(
            "current-backlog",
            this,
            () => Volatile.Read(ref _currentBacklog))
        {
            DisplayName = "Current Backlog",
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
    internal void ConnectFailure(ServerAddress serverAddress, EndPoint remoteNetworkAddress, Exception exception)
    {
        Interlocked.Increment(ref _totalFailedConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectFailure(
                serverAddress.ToString(),
                remoteNetworkAddress?.ToString(),
                exception.GetType().FullName,
                exception.ToString());
        }
    }

    [NonEvent]
    internal void ConnectStart(ServerAddress serverAddress, EndPoint remoteNetworkAddress)
    {
        Interlocked.Increment(ref _currentBacklog);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectStart(serverAddress.ToString(), remoteNetworkAddress?.ToString());
        }
    }

    [NonEvent]
    internal void ConnectStop(ServerAddress serverAddress, EndPoint remoteNetworkAddress)
    {
        Interlocked.Decrement(ref _currentBacklog);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectStop(serverAddress.ToString(), remoteNetworkAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectSuccess(ServerAddress serverAddress, EndPoint remoteNetworkAddress)
    {
        Interlocked.Increment(ref _currentConnections);
        if (IsEnabled(EventLevel.Error, EventKeywords.None))
        {
            ConnectSuccess(serverAddress.ToString(), remoteNetworkAddress.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionFailure(
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress,
        Exception exception)
    {
        Interlocked.Increment(ref _totalFailedConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionFailure(
                serverAddress.ToString(),
                remoteNetworkAddress.ToString(),
                exception.GetType().FullName,
                exception.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStart(ServerAddress serverAddress, EndPoint remoteNetworkAddress)
    {
        Interlocked.Increment(ref _totalConnections);
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStart(serverAddress.ToString(), remoteNetworkAddress?.ToString());
        }
    }

    [NonEvent]
    internal void ConnectionStop(ServerAddress serverAddress, EndPoint remoteNetworkAddress)
    {
        if (IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            ConnectionStop(serverAddress.ToString(), remoteNetworkAddress.ToString());
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _currentConnectionsCounter.Dispose();
        _currentBacklogCounter.Dispose();
        _connectionsPerSecondCounter.Dispose();
        _totalConnectionsCounter.Dispose();
        _totalFailedConnectionsCounter.Dispose();
        base.Dispose(disposing);
    }

    // Event methods sorted by eventId

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    private void ConnectionStart(
        string serverAddress,
        string? remoteNetworkAddress) =>
        WriteEvent(1, serverAddress, remoteNetworkAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ConnectionStop(string serverAddress, string? remoteNetworkAddress) =>
        WriteEvent(2, serverAddress, remoteNetworkAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(3, Level = EventLevel.Error)]
    private void ConnectionFailure(
        string serverAddress,
        string? remoteNetworkAddress,
        string? exceptionType,
        string exceptionDetails) =>
        WriteEvent(
            3,
            serverAddress,
            remoteNetworkAddress,
            exceptionType,
            exceptionDetails);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(4, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    private void ConnectStart(string serverAddress, string? remoteNetworkAddress) =>
        WriteEvent(4, serverAddress, remoteNetworkAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(5, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    private void ConnectStop(string serverAddress, string? remoteNetworkAddress) =>
        WriteEvent(5, serverAddress, remoteNetworkAddress);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(6, Level = EventLevel.Error)]
    private void ConnectFailure(
        string serverAddress,
        string? remoteNetworkAddress,
        string? exceptionType,
        string exceptionDetails) =>
        WriteEvent(
            6,
            serverAddress,
            remoteNetworkAddress,
            exceptionType,
            exceptionDetails);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [Event(7, Level = EventLevel.Informational)]
    private void ConnectSuccess(string serverAddress, string? remoteNetworkAddress) =>
        WriteEvent(7, serverAddress, remoteNetworkAddress);
}
