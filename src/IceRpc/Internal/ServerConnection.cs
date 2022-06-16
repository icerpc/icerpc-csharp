// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A connection created by a <see cref="Server"/>.</summary>
internal sealed class ServerConnection : IConnection
{
    /// <inheritdoc/>
    public bool IsResumable => false;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation => _core.NetworkConnectionInformation;

    /// <inheritdoc/>
    public Protocol Protocol { get; }

    private readonly CancellationTokenSource _connectCancellationSource = new();

    private Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    private readonly ConnectionCore _core;

    private readonly object _mutex = new();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _core.InvokeAsync(this, request, cancel);

    /// <inheritdoc/>
    public void OnClose(Action<IConnection, Exception> callback) => _core.OnClose(this, callback);

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(Protocol protocol, ConnectionOptions options)
    {
        Protocol = protocol;
        _core = new ConnectionCore(options);
        _connectTimeout = options.ConnectTimeout;
    }

    /// <summary>Aborts the connection.</summary>
    internal void Abort() => _core.Abort(this);

    /// <summary>Establishes a connection.</summary>
    /// <param name="networkConnection">The underlying network connection.</param>
    /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
    internal Task ConnectAsync<T>(T networkConnection, IProtocolConnectionFactory<T> protocolConnectionFactory)
        where T : INetworkConnection
    {
        lock (_mutex)
        {
            _connectTask ??= _core.ConnectAsync(
                this,
                isServer: true,
                networkConnection,
                protocolConnectionFactory,
                _connectCancellationSource.Token);
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

        await _core.ShutdownAsync(this, message, cancel).ConfigureAwait(false);
    }
}
