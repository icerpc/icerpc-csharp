// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

/// <summary>This enumeration describes the multiplexed transport operations. It's used as a set of flags to specify an
/// enumeration value for multiple operations.</summary>
[Flags]
public enum MultiplexedTransportOperation
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
/// cref="IMultiplexedConnection" /> client connections. It also provides access to the last created connection.
/// </summary>
public sealed class TestMultiplexedClientTransportDecorator : IMultiplexedClientTransport
{
    /// <summary>The last created connection.</summary>
    public TestMultiplexedConnectionDecorator LastCreatedConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    private readonly IMultiplexedClientTransport _decoratee;
    private readonly MultiplexedTransportOperation _failOperations;
    private readonly Exception _failureException = new();
    private readonly MultiplexedTransportOperation _holdOperations;
    private TestMultiplexedConnectionDecorator? _lastConnection;

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

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
            _holdOperations,
            _failOperations,
            _failureException);
        _lastConnection = connection;
        return connection;
    }

    /// <summary>Constructs a <see cref="TestMultiplexedClientTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated client transport.</param>
    /// <param name="holdOperations">The operations to hold for connection created with this transport.</param>
    /// <param name="failOperations">The operations that will fail for connections created with this transport.</param>
    /// <param name="failureException">The exception to raise for operations configured to fail. If not specified, an
    /// <see cref="IceRpcException" /> exception with the <see cref="IceRpcError.IceRpcError" /> error code is
    /// used.</param>
    public TestMultiplexedClientTransportDecorator(
        IMultiplexedClientTransport decoratee,
        MultiplexedTransportOperation holdOperations = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation failOperations = MultiplexedTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        _holdOperations = holdOperations;
        _failOperations = failOperations;
        _failureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
    }
}

/// <summary>A <see cref="IMultiplexedServerTransport" /> decorator to create decorated <see
/// cref="IMultiplexedConnection" /> server connections. It also provides access to the last accepted connection and
/// allows to configure the behavior of the listener and the operations inherited by connections.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestMultiplexedServerTransportDecorator : IMultiplexedServerTransport
#pragma warning restore CA1001
{
    /// <summary>The last accepted connection.</summary>
    public TestMultiplexedConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    /// <summary>The <see cref="TestTransportOperationHelper{MultiplexedTransportOperation}" /> used by the server
    /// transport <see cref="IListener{IMultiplexedConnection}" /> operations and inherited by accepted
    /// connections.</summary>
    public TestTransportOperationHelper<MultiplexedTransportOperation> Operations { get; }

    private readonly IMultiplexedServerTransport _decoratee;
    private TestMultiplexedListenerDecorator? _listener;

    /// <summary>Constructs a <see cref="TestMultiplexedServerTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated server transport.</param>
    /// <param name="holdOperations">The operations to hold for connection created with this transport.</param>
    /// <param name="failOperations">The operations that will fail for connections created with this transport.</param>
    /// <param name="failureException">The exception to raise for operations configured to fail. If not specified, an
    /// <see cref="IceRpcException" /> exception with the <see cref="IceRpcError.IceRpcError" /> error code is
    /// used.</param>
    public TestMultiplexedServerTransportDecorator(
        IMultiplexedServerTransport decoratee,
        MultiplexedTransportOperation holdOperations = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation failOperations = MultiplexedTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperations, failOperations, failureException);
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
            Operations);
        return _listener;
    }

    private class TestMultiplexedListenerDecorator : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal TestMultiplexedConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IMultiplexedConnection> _decoratee;
        private TestMultiplexedConnectionDecorator? _lastAcceptedConnection;
        private readonly TestTransportOperationHelper<MultiplexedTransportOperation> _operations;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            await _operations.CheckAsync(MultiplexedTransportOperation.Accept, cancellationToken);

            (IMultiplexedConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _operations.CheckAsync(MultiplexedTransportOperation.Accept, cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            LastAcceptedConnection = new TestMultiplexedConnectionDecorator(
                connection,
                _operations.Hold,
                _operations.Fail,
                _operations.FailureException);
            return (LastAcceptedConnection, remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal TestMultiplexedListenerDecorator(
            IListener<IMultiplexedConnection> decoratee,
            TestTransportOperationHelper<MultiplexedTransportOperation> operations)
        {
            _decoratee = decoratee;
            _operations = operations;
        }
    }
}

/// <summary>An <see cref="IMultiplexedConnection" /> decorator to configure the behavior of connection operations and
/// provide access to the last created <see cref="IMultiplexedStream" /> stream.</summary>
public sealed class TestMultiplexedConnectionDecorator : IMultiplexedConnection
{
    public TestMultiplexedStreamDecorator LastStream
    {
        get => _lastStream ?? throw new InvalidOperationException("No stream created yet.");
        set => _lastStream = value;
    }

    /// <summary>The <see cref="TestTransportOperationHelper{MultiplexedTransportOperation}" /> used by this connection
    /// operations and inherited by streams created or accepted by this connection.</summary>
    public TestTransportOperationHelper<MultiplexedTransportOperation> Operations { get; }

    private readonly IMultiplexedConnection _decoratee;
    private TestMultiplexedStreamDecorator? _lastStream;

    /// <inheritdoc/>
    public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.AcceptStream, cancellationToken);

        var stream = new TestMultiplexedStreamDecorator(
            await _decoratee.AcceptStreamAsync(cancellationToken),
            Operations.Hold,
            Operations.Fail,
            Operations.FailureException);
        _lastStream = stream;

        // Check again fail/hold condition in case the configuration was changed while AcceptStreamAsync was pending.
        await Operations.CheckAsync(MultiplexedTransportOperation.AcceptStream, cancellationToken);

        return stream;
    }

    /// <inheritdoc/>
    public async ValueTask<IMultiplexedStream> CreateStreamAsync(
        bool bidirectional,
        CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.CreateStream, cancellationToken);

        var stream = new TestMultiplexedStreamDecorator(
            await _decoratee.CreateStreamAsync(bidirectional, cancellationToken),
            Operations.Hold,
            Operations.Fail,
            Operations.FailureException);
        _lastStream = stream;
        return stream;
    }

    /// <inheritdoc/>
    public async Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.Close, cancellationToken);

        await _decoratee.CloseAsync(closeError, cancellationToken);

        // Release CreateStream and AcceptStream
        Operations.Hold &= ~(MultiplexedTransportOperation.CreateStream | MultiplexedTransportOperation.AcceptStream);
    }

    /// <inheritdoc/>
    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.Connect, cancellationToken);
        return await _decoratee.ConnectAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.Dispose, CancellationToken.None);
        await _decoratee.DisposeAsync();
        Operations.Complete();
    }

    internal TestMultiplexedConnectionDecorator(
        IMultiplexedConnection decoratee,
        MultiplexedTransportOperation holdOperation = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation failOperation = MultiplexedTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperation, failOperation, failureException);
    }
}

public sealed class TestMultiplexedStreamDecorator : IMultiplexedStream
{
    /// <summary>The <see cref="TestTransportOperationHelper{MultiplexedTransportOperation}" /> used by this stream
    /// <see cref="Input"/> and <see cref="Output"/> operations.</summary>
    public TestTransportOperationHelper<MultiplexedTransportOperation> Operations { get; }

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
        MultiplexedTransportOperation holdOperation = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation failOperation = MultiplexedTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperation, failOperation, failureException);
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

internal sealed class TestPipeWriter : PipeWriter
{
    private readonly PipeWriter _decoratee;
    private readonly TestTransportOperationHelper<MultiplexedTransportOperation> _operations;

    public override void Advance(int bytes) => _decoratee.Advance(bytes);

    public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

    public override void Complete(Exception? exception)
    {
        _operations.Hold &= ~MultiplexedTransportOperation.StreamWrite;
        _decoratee.Complete(exception);
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
    {
        await _operations.CheckAsync(MultiplexedTransportOperation.StreamWrite, cancellationToken);
        return await _decoratee.FlushAsync(cancellationToken);
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken)
    {
        await _operations.CheckAsync(MultiplexedTransportOperation.StreamWrite, cancellationToken);
        return await _decoratee.WriteAsync(source, cancellationToken);
    }

    internal TestPipeWriter(
        PipeWriter decoratee,
        TestTransportOperationHelper<MultiplexedTransportOperation> operations)
    {
        _decoratee = decoratee;
        _operations = operations;
    }
}

internal sealed class TestPipeReader : PipeReader
{
    private readonly PipeReader _decoratee;
    private readonly TestTransportOperationHelper<MultiplexedTransportOperation> _operations;

    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        _operations.Hold &= ~MultiplexedTransportOperation.StreamRead;
        _decoratee.Complete(exception);
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        await _operations.CheckAsync(MultiplexedTransportOperation.StreamRead, cancellationToken);

        ReadResult result = await _decoratee.ReadAsync(cancellationToken);

        // Check again fail/hold condition in case the configuration was changed while ReadAsync was pending.
        await _operations.CheckAsync(MultiplexedTransportOperation.StreamRead, cancellationToken);

        return result;
    }

    public override bool TryRead(out ReadResult result)
    {
        result = new ReadResult();
        return false;
    }

    internal TestPipeReader(
        PipeReader decoratee,
        TestTransportOperationHelper<MultiplexedTransportOperation> operations)
    {
        _decoratee = decoratee;
        _operations = operations;
    }
}

public static class TestMultiplexedTransportServiceCollectionExtensions
{
    /// <summary>Installs the test multiplexed transport.</summary>
    public static IServiceCollection AddTestMultiplexedTransport(
        this IServiceCollection services,
        MultiplexedTransportOperation clientHoldOperation = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation clientFailOperation = MultiplexedTransportOperation.None,
        Exception? clientFailureException = null,
        MultiplexedTransportOperation serverHoldOperation = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation serverFailOperation = MultiplexedTransportOperation.None,
        Exception? serverFailureException = null) => services
            .AddColocTransport()
            .AddSingleton(provider =>
                new TestMultiplexedClientTransportDecorator(
                    new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()),
                    holdOperations: clientHoldOperation,
                    failOperations: clientFailOperation,
                    failureException: clientFailureException))
            .AddSingleton<IMultiplexedClientTransport>(provider =>
                provider.GetRequiredService<TestMultiplexedClientTransportDecorator>())
            .AddSingleton(provider =>
                new TestMultiplexedServerTransportDecorator(
                    new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()),
                    holdOperations: serverHoldOperation,
                    failOperations: serverFailOperation,
                    failureException: serverFailureException))
            .AddSingleton<IMultiplexedServerTransport>(provider =>
                provider.GetRequiredService<TestMultiplexedServerTransportDecorator>());
}
