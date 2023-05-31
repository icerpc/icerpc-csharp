// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>A helper class to connect and provide access to a client and server multiplexed connections. It also
/// ensures the connections are correctly disposed.</summary>
public sealed class ClientServerMultiplexedConnection : IAsyncDisposable
{
    /// <summary>Gets the client connection.</summary>
    public IMultiplexedConnection Client { get; }

    /// <summary>Gets the server connection.</summary>
    public IMultiplexedConnection Server
    {
        get => _server ?? throw new InvalidOperationException("server connection not initialized");
        private set => _server = value;
    }

    private readonly IListener<IMultiplexedConnection> _listener;
    private IMultiplexedConnection? _server;

    /// <summary>Accepts and connects the server connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The accepted server connection and connection information.</returns>
    public async Task<TransportConnectionInformation> AcceptAsync(CancellationToken cancellationToken = default)
    {
        (_server, _) = await _listener.AcceptAsync(cancellationToken);
        return await _server.ConnectAsync(cancellationToken);
    }

    /// <summary>Connects the client connection and accepts and connects the server connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when both connections are connected.</returns>
    public Task AcceptAndConnectAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(AcceptAsync(cancellationToken), Client.ConnectAsync(cancellationToken));

    /// <summary>Creates and accepts a stream with the client and server connections.</summary>
    /// <param name="bidirectional"><see langword="true"/> to create a bidirectional stream; otherwise,
    /// <see langword="false"/>.</param>
    /// <param name="createWithServerConnection">Creates the stream with the server connection instead of the client
    /// connection.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when both connections are connected.</returns>
    public async Task<LocalAndRemoteMultiplexedStreams> CreateAndAcceptStreamAsync(
        bool bidirectional = true,
        bool createWithServerConnection = false,
        CancellationToken cancellationToken = default)
    {
        IMultiplexedConnection createStreamConnection = createWithServerConnection ? Server : Client;
        IMultiplexedConnection acceptStreamConnection = createWithServerConnection ? Client : Server;

        IMultiplexedStream localStream = await createStreamConnection.CreateStreamAsync(
            bidirectional: bidirectional,
            default).ConfigureAwait(false);
        _ = await localStream.Output.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0xFF }), cancellationToken);
        IMultiplexedStream remoteStream = await acceptStreamConnection.AcceptStreamAsync(cancellationToken);
        ReadResult readResult = await remoteStream.Input.ReadAsync(cancellationToken);
        remoteStream.Input.AdvanceTo(readResult.Buffer.End);
        return new LocalAndRemoteMultiplexedStreams(localStream, remoteStream);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
        await _listener.DisposeAsync();
    }

    /// <summary>Constructs a new <see cref="ClientServerMultiplexedConnection"/>.</summary>
    /// <param name="clientConnection">The client connection.</param>
    /// <param name="listener">The listener.</param>
    public ClientServerMultiplexedConnection(
        IMultiplexedConnection clientConnection,
        IListener<IMultiplexedConnection> listener)
    {
        _listener = listener;
        Client = clientConnection;
    }
}

/// <summary>A helper class to provide access to a local and remote stream. It also ensures the streams are correctly
/// completed.</summary>
public sealed class LocalAndRemoteMultiplexedStreams : IDisposable
{
    /// <summary>The local stream.</summary>
    public IMultiplexedStream Local { get; }

    /// <summary>The remote stream.</summary>
    public IMultiplexedStream Remote { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        CleanupStream(Local);
        CleanupStream(Remote);
    }

    internal LocalAndRemoteMultiplexedStreams(IMultiplexedStream localStream, IMultiplexedStream remoteStream)
    {
        Local = localStream;
        Remote = remoteStream;
    }

    private static void CleanupStream(IMultiplexedStream stream)
    {
        if (stream.IsBidirectional)
        {
            stream.Output.Complete();
            stream.Input.Complete();
        }
        else if (stream.IsRemote)
        {
            stream.Input.Complete();
        }
        else
        {
            stream.Output.Complete();
        }
    }
}
