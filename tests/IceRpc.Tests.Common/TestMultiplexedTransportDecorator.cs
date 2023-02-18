// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

/// <summary>This enumeration describes the multiplexed transport operations.</summary>
[Flags]
public enum MultiplexedTransportOperations
{
    /// <summary>The no-operation enumerator value.</summary>
    None = 0,

    /// <summary>The <see cref="IListener{IMultiplexedConnection}.AcceptAsync" /> operation.</summary>
    Accept = 1,

    /// <summary>The <see cref="IMultiplexedConnection.AcceptStreamAsync" /> operation.</summary>
    AcceptStream = 2,

    /// <summary>The <see cref="IMultiplexedConnection.CreateStreamAsync" /> operation.</summary>
    CreateStream = 4,

    /// <summary>The <see cref="IMultiplexedConnection.ConnectAsync" /> operation.</summary>
    Connect = 8,

    /// <summary>The <see cref="IMultiplexedConnection.CloseAsync" /> operation.</summary>
    Close = 16,

    /// <summary>The <see cref="IAsyncDisposable.DisposeAsync" /> operation.</summary>
    Dispose = 32,

    /// <summary>The <see cref="PipeReader.ReadAsync" /> operation.</summary>
    StreamRead = 64,

    /// <summary>The <see cref="PipeWriter.WriteAsync" /> or <see cref="PipeWriter.FlushAsync" /> operation.</summary>
    StreamWrite = 128,
}

/// <summary>A <see cref="IMultiplexedClientTransport" /> decorator to create decorated <see
/// cref="IMultiplexedConnection" /> client connections and to get to the last created connection.</summary>
public sealed class TestMultiplexedClientTransportDecorator : IMultiplexedClientTransport
{
    /// <summary>The operations options used to create client connections.</summary>
    public TransportOperationsOptions<MultiplexedTransportOperations> ConnectionOperationsOptions { get; set; }

    /// <summary>The last created connection.</summary>
    public TestMultiplexedConnectionDecorator LastCreatedConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    private readonly IMultiplexedClientTransport _decoratee;
    private TestMultiplexedConnectionDecorator? _lastConnection;
    private Action<TestMultiplexedConnectionDecorator>? _onCreateConnection;

    /// <summary>Constructs a <see cref="TestMultiplexedClientTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated client transport.</param>
    /// <param name="operationsOptions">The transport operations options.</param>
    public TestMultiplexedClientTransportDecorator(
        IMultiplexedClientTransport decoratee,
        TransportOperationsOptions<MultiplexedTransportOperations>? operationsOptions = null)
    {
        _decoratee = decoratee;
        ConnectionOperationsOptions = operationsOptions ?? new();
    }

    /// <inheritdoc/>
    public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

    /// <inheritdoc/>
    public IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        var connection = new TestMultiplexedConnectionDecorator(
            _decoratee.CreateConnection(serverAddress, options, clientAuthenticationOptions),
            ConnectionOperationsOptions);
        _lastConnection = connection;
        _onCreateConnection?.Invoke(connection);
        return connection;
    }

    /// <summary>Sets a callback to be notified when a connection is created.</summary>
    /// <param name="onCreateConnection">The callback action.</param>
    public void OnCreateConnection(Action<TestMultiplexedConnectionDecorator> onCreateConnection) =>
        _onCreateConnection = onCreateConnection;
}

/// <summary>A <see cref="IMultiplexedServerTransport" /> decorator to create decorated <see
/// cref="IMultiplexedConnection" /> server connections and to get the last accepted connection.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestMultiplexedServerTransportDecorator : IMultiplexedServerTransport
#pragma warning restore CA1001
{
    /// <summary>The operations options used to create server connections.</summary>
    public TransportOperationsOptions<MultiplexedTransportOperations> ConnectionOperationsOptions
    {
        get => _listener?.ConnectionOperationsOptions ?? _connectionOperationsOptions;

        set
        {
            if (_listener is null)
            {
                _connectionOperationsOptions = value;
            }
            else
            {
                _listener.ConnectionOperationsOptions = value;
            }
        }
    }

    /// <summary>The last accepted connection.</summary>
    public TestMultiplexedConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    /// <summary>The <see cref="TransportOperations{MultiplexedTransportOperations}" /> used by the <see
    /// cref="IListener{IMultiplexedConnection}" /> operations.</summary>
    public TransportOperations<MultiplexedTransportOperations> ListenerOperations { get; }

    private TransportOperationsOptions<MultiplexedTransportOperations> _connectionOperationsOptions;
    private readonly IMultiplexedServerTransport _decoratee;
    private TestMultiplexedListenerDecorator? _listener;

    /// <summary>Constructs a <see cref="TestMultiplexedServerTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated server transport.</param>
    /// <param name="operationsOptions">The transport operations options.</param>
    public TestMultiplexedServerTransportDecorator(
        IMultiplexedServerTransport decoratee,
        TransportOperationsOptions<MultiplexedTransportOperations>? operationsOptions = null)
    {
        _decoratee = decoratee;
        _connectionOperationsOptions = operationsOptions ?? new();
        ListenerOperations = new(_connectionOperationsOptions);
    }

    /// <inheritdoc/>
    public IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("Test server transport doesn't support multiple listeners.");
        }
        _listener = new TestMultiplexedListenerDecorator(
            _decoratee.Listen(serverAddress, options, serverAuthenticationOptions),
            ListenerOperations,
            _connectionOperationsOptions);
        return _listener;
    }

    private class TestMultiplexedListenerDecorator : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal TransportOperationsOptions<MultiplexedTransportOperations> ConnectionOperationsOptions { get; set; }

        internal TestMultiplexedConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IMultiplexedConnection> _decoratee;
        private TestMultiplexedConnectionDecorator? _lastAcceptedConnection;
        private readonly TransportOperations<MultiplexedTransportOperations> _listenerOperations;

        public Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken) =>
            _listenerOperations.CallAsync(
                MultiplexedTransportOperations.Accept,
                async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> () =>
                {
                    (IMultiplexedConnection connection, EndPoint remoteNetworkAddress) =
                        await _decoratee.AcceptAsync(cancellationToken);
                    LastAcceptedConnection = new TestMultiplexedConnectionDecorator(
                        connection,
                        ConnectionOperationsOptions);
                    if (_listenerOperations.Fail.HasFlag(MultiplexedTransportOperations.Accept))
                    {
                        // Dispose the connection if the operation is going to fail after completion.
                        await connection.DisposeAsync();
                    }
                    return (LastAcceptedConnection, remoteNetworkAddress);
                },
                cancellationToken);

        public ValueTask DisposeAsync() =>
            _listenerOperations.CallDisposeAsync(MultiplexedTransportOperations.Dispose, _decoratee);

        internal TestMultiplexedListenerDecorator(
            IListener<IMultiplexedConnection> decoratee,
            TransportOperations<MultiplexedTransportOperations> operations,
            TransportOperationsOptions<MultiplexedTransportOperations> connectionOperationsOptions)
        {
            _decoratee = decoratee;
            ConnectionOperationsOptions = connectionOperationsOptions;
            _listenerOperations = operations;
        }
    }
}

/// <summary>An <see cref="IMultiplexedConnection" /> decorator to configure the behavior of connection operations and
/// provide access to the last created <see cref="IMultiplexedStream" /> stream.</summary>
public sealed class TestMultiplexedConnectionDecorator : IMultiplexedConnection
{
    /// <summary>The last stream either created or accepted by this connection.</summary>
    public TestMultiplexedStreamDecorator LastStream
    {
        get => _lastStream ?? throw new InvalidOperationException("No stream created yet.");
        set => _lastStream = value;
    }

    /// <summary>The operations options used to create streams.</summary>
    public TransportOperationsOptions<MultiplexedTransportOperations> StreamOperationsOptions { get; set; }

    /// <summary>The <see cref="TransportOperations{MultiplexedTransportOperations}" /> used by this connection
    /// operations.</summary>
    public TransportOperations<MultiplexedTransportOperations> Operations { get; }

    private readonly IMultiplexedConnection _decoratee;
    private TestMultiplexedStreamDecorator? _lastStream;
    private Action<TestMultiplexedStreamDecorator>? _onAcceptStream;
    private Action<TestMultiplexedStreamDecorator>? _onCreateStream;

    /// <inheritdoc/>
    public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
        Operations.CallAsync(
            MultiplexedTransportOperations.AcceptStream,
            async ValueTask<IMultiplexedStream> () =>
            {
                var stream = new TestMultiplexedStreamDecorator(
                    await _decoratee.AcceptStreamAsync(cancellationToken),
                    StreamOperationsOptions);
                _lastStream = stream;
                if (Operations.Fail.HasFlag(MultiplexedTransportOperations.AcceptStream))
                {
                    // Cleanup the stream if the operation is not configure to fail.
                    if (stream.IsBidirectional)
                    {
                        stream.Output.Complete();
                    }
                    stream.Input.Complete();
                }
                _onAcceptStream?.Invoke(_lastStream);
                return stream;
            },
            cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IMultiplexedStream> CreateStreamAsync(
        bool bidirectional,
        CancellationToken cancellationToken) =>
        Operations.CallAsync(
            MultiplexedTransportOperations.CreateStream,
            async ValueTask<IMultiplexedStream> () =>
            {
                var stream = new TestMultiplexedStreamDecorator(
                    await _decoratee.CreateStreamAsync(bidirectional, cancellationToken),
                    StreamOperationsOptions);
                _lastStream = stream;
                if (Operations.Fail.HasFlag(MultiplexedTransportOperations.CreateStream))
                {
                    // Cleanup the stream if the operation is not configure to fail.
                    stream.Output.Complete();
                    if (stream.IsBidirectional)
                    {
                        stream.Input.Complete();
                    }
                }
                _onCreateStream?.Invoke(_lastStream);
                return stream;
            },
            cancellationToken);

    /// <inheritdoc/>
    public Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken) =>
        Operations.CallAsync(
            MultiplexedTransportOperations.Close,
            async Task () =>
            {
                await _decoratee.CloseAsync(closeError, cancellationToken);

                // Release CreateStream and AcceptStream
                Operations.Hold &=
                    ~(MultiplexedTransportOperations.CreateStream | MultiplexedTransportOperations.AcceptStream);
            },
            cancellationToken);

    /// <inheritdoc/>
    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        Operations.CallAsync(
            MultiplexedTransportOperations.Connect,
            () => _decoratee.ConnectAsync(cancellationToken),
            cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => Operations.CallDisposeAsync(MultiplexedTransportOperations.Dispose, _decoratee);

    /// <summary>Sets a callback to be notified when a stream is accepted.</summary>
    /// <param name="onAcceptStream">The callback action.</param>
    public void OnAcceptStream(Action<TestMultiplexedStreamDecorator> onAcceptStream) =>
        _onAcceptStream = onAcceptStream;

    /// <summary>Sets a callback to be notified when a stream is created.</summary>
    /// <param name="onCreateStream">The callback action.</param>
    public void OnCreateStream(Action<TestMultiplexedStreamDecorator> onCreateStream) =>
        _onCreateStream = onCreateStream;

    internal TestMultiplexedConnectionDecorator(
        IMultiplexedConnection decoratee,
        TransportOperationsOptions<MultiplexedTransportOperations> operationsOptions)
    {
        _decoratee = decoratee;
        StreamOperationsOptions = operationsOptions;
        Operations = new(operationsOptions);
    }
}

/// <summary>An <see cref="IMultiplexedStream" /> decorator to configure the behavior of stream input and output
/// operations.</summary>
public sealed class TestMultiplexedStreamDecorator : IMultiplexedStream
{
    /// <summary>The <see cref="TransportOperations{MultiplexedTransportOperation}" /> used by this stream
    /// <see cref="Input"/> and <see cref="Output"/> operations.</summary>
    public TransportOperations<MultiplexedTransportOperations> Operations { get; }

    /// <inheritdoc/>
    public ulong Id => _decoratee.Id;

    /// <inheritdoc/>
    public PipeReader Input => _input ?? throw new InvalidOperationException("No input for unidirectional stream.");

    /// <inheritdoc/>
    public bool IsBidirectional => _decoratee.IsBidirectional;

    /// <inheritdoc/>
    public bool IsRemote => _decoratee.IsRemote;

    /// <inheritdoc/>
    public bool IsStarted => _decoratee.IsStarted;

    /// <inheritdoc/>
    public PipeWriter Output => _output ?? throw new InvalidOperationException("No output for unidirectional stream.");

    /// <inheritdoc/>
    public Task ReadsClosed => _decoratee.ReadsClosed;

    /// <inheritdoc/>
    public Task WritesClosed => _decoratee.WritesClosed;

    private readonly IMultiplexedStream _decoratee;
    private readonly TestPipeReader? _input;
    private readonly TestPipeWriter? _output;

    internal TestMultiplexedStreamDecorator(
        IMultiplexedStream decoratee,
        TransportOperationsOptions<MultiplexedTransportOperations> operationsOptions)
    {
        _decoratee = decoratee;
        Operations = new(operationsOptions);
        if (!IsRemote || IsBidirectional)
        {
            _output = new TestPipeWriter(decoratee.Output, Operations);
        }
        if (IsRemote || IsBidirectional)
        {
            _input = new TestPipeReader(decoratee.Input, Operations);
        }
    }
}

internal sealed class TestPipeWriter : ReadOnlySequencePipeWriter
{
    private readonly PipeWriter _decoratee;
    private readonly TransportOperations<MultiplexedTransportOperations> _operations;

    public override void Advance(int bytes) => _decoratee.Advance(bytes);

    public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

    public override void Complete(Exception? exception)
    {
        _operations.Hold &= ~MultiplexedTransportOperations.StreamWrite;
        _decoratee.Complete(exception);
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
        _operations.CallAsync(
            MultiplexedTransportOperations.StreamWrite,
            () => _decoratee.FlushAsync(cancellationToken),
            cancellationToken);

    public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken) =>
        _operations.CallAsync(
            MultiplexedTransportOperations.StreamWrite,
            () => _decoratee.WriteAsync(source, cancellationToken),
            cancellationToken);

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken) =>
        _operations.CallAsync(
            MultiplexedTransportOperations.StreamWrite,
            async ValueTask<FlushResult> () =>
            {
                if (_decoratee is ReadOnlySequencePipeWriter readonlySequenceWriter)
                {
                    return await readonlySequenceWriter.WriteAsync(
                        source,
                        endStream,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    FlushResult flushResult = default;
                    foreach (ReadOnlyMemory<byte> buffer in source)
                    {
                        flushResult = await _decoratee.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (flushResult.IsCompleted || flushResult.IsCanceled)
                        {
                            break;
                        }
                    }
                    return flushResult;
                }
            },
            cancellationToken);

    internal TestPipeWriter(
        PipeWriter decoratee,
        TransportOperations<MultiplexedTransportOperations> operations)
    {
        _decoratee = decoratee;
        _operations = operations;
    }
}

internal sealed class TestPipeReader : PipeReader
{
    private readonly PipeReader _decoratee;
    private readonly TransportOperations<MultiplexedTransportOperations> _operations;

    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        _operations.Hold &= ~MultiplexedTransportOperations.StreamRead;
        _decoratee.Complete(exception);
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken) =>
        _operations.CallAsync(
            MultiplexedTransportOperations.StreamRead,
            () => _decoratee.ReadAsync(cancellationToken),
            cancellationToken);

    public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);

    internal TestPipeReader(
        PipeReader decoratee,
        TransportOperations<MultiplexedTransportOperations> operations)
    {
        _decoratee = decoratee;
        _operations = operations;
    }
}

/// <summary>Extension methods for setting up the test multiplexed transport in an <see cref="IServiceCollection"
/// />.</summary>
public static class TestMultiplexedTransportServiceCollectionExtensions
{
    /// <summary>Installs the test multiplexed transport.</summary>
    public static IServiceCollection AddTestMultiplexedTransport(
        this IServiceCollection services,
        TransportOperationsOptions<MultiplexedTransportOperations>? clientOperationsOptions = null,
        TransportOperationsOptions<MultiplexedTransportOperations>? serverOperationsOptions = null) => services
            .AddColocTransport()
            .AddSingleton(provider =>
                new TestMultiplexedClientTransportDecorator(
                    new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()),
                    clientOperationsOptions))
            .AddSingleton<IMultiplexedClientTransport>(provider =>
                provider.GetRequiredService<TestMultiplexedClientTransportDecorator>())
            .AddSingleton(provider =>
                new TestMultiplexedServerTransportDecorator(
                    new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()),
                    serverOperationsOptions))
            .AddSingleton<IMultiplexedServerTransport>(provider =>
                provider.GetRequiredService<TestMultiplexedServerTransportDecorator>());
}
