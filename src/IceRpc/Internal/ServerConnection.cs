// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A connection created by a <see cref="Server"/>.</summary>
internal sealed class ServerConnection : IConnection
{
    /// <inheritdoc/>
    public bool IsResumable => false;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

    /// <inheritdoc/>
    public Protocol Protocol => _protocolConnection.Protocol;

    private readonly CancellationTokenSource _connectCancellationSource = new();

    private Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _protocolConnection.InvokeAsync(this, request, cancel);

    /// <inheritdoc/>
    public void OnClose(Action<Exception> callback) => _protocolConnection.OnClose(callback);

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(IProtocolConnection protocolConnection, TimeSpan connectTimeout)
    {
        _protocolConnection = protocolConnection;
        _connectTimeout = connectTimeout;
    }

    /// <summary>Aborts the connection.</summary>
    internal void Abort() => _protocolConnection.Abort(new ConnectionAbortedException());

    /// <summary>Establishes the connection.</summary>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    internal Task ConnectAsync()
    {
        lock (_mutex)
        {
            _connectTask ??= _protocolConnection.ConnectAsync(this, isServer: true, _connectCancellationSource.Token);
        }

        _connectCancellationSource.CancelAfter(_connectTimeout);

        return _connectTask;
    }

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal async Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            // Make sure that connection establishment is not initiated if it's not in progress or completed.
            _connectTask ??= Task.FromException(new ConnectionClosedException());
        }

        // TODO: Should we actually cancel the pending connect on ShutdownAsync?
        // try
        // {
        //     _connectCancellationSource.Cancel();
        // }
        // catch
        // {
        // }

        try
        {
            // Wait for connection establishment to complete before calling ShutdownAsync.
            await _connectTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore
        }

        await _protocolConnection.ShutdownAsync(message, cancel).ConfigureAwait(false);
    }
}
