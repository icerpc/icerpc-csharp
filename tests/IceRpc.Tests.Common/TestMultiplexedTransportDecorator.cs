// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

[Flags]
public enum MultiplexedTransportOperation
{
    None = 0,
    Accept = 1,
    AcceptStream = 2,
    CreateStream = 4,
    Connect = 8,
    Close = 16,
    Dispose = 32,
    StreamRead = 64,
    StreamWrite = 128,
}

#pragma warning disable CA1001 // _lastConnection is disposed by the caller.
public sealed class TestMultiplexedClientTransportDecorator : IMultiplexedClientTransport
#pragma warning restore CA1001
{
    public TestMultiplexedConnectionDecorator LastConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    private readonly IMultiplexedClientTransport _decoratee;
    private readonly MultiplexedTransportOperation _failOperation;
    private readonly Exception _failureException = new();
    private readonly MultiplexedTransportOperation _holdOperation;
    private TestMultiplexedConnectionDecorator? _lastConnection;

    public string Name => _decoratee.Name;

    public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

    public IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        var connection = new TestMultiplexedConnectionDecorator(_decoratee.CreateConnection(
            serverAddress,
            options,
            clientAuthenticationOptions),
            _holdOperation,
            _failOperation,
            _failureException);
        _lastConnection = connection;
        return connection;
    }

    public TestMultiplexedClientTransportDecorator(
        IMultiplexedClientTransport decoratee,
        MultiplexedTransportOperation holdOperation = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation failOperation = MultiplexedTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        _holdOperation = holdOperation;
        _failOperation = failOperation;
        _failureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
    }
}

/// <summary>A decorator for Multiplexed server transport that holds any ConnectAsync and ShutdownAsync for connections
/// accepted by this transport.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestMultiplexedServerTransportDecorator : IMultiplexedServerTransport
#pragma warning restore CA1001
{
    public TestTransportOperationHelper<MultiplexedTransportOperation> Operations { get; }

    public string Name => _decoratee.Name;

    public TestMultiplexedConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    private readonly IMultiplexedServerTransport _decoratee;
    private TestMultiplexedListenerDecorator? _listener;

    public TestMultiplexedServerTransportDecorator(
        IMultiplexedServerTransport decoratee,
        MultiplexedTransportOperation holdOperation = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation failOperation = MultiplexedTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperation, failOperation, failureException);
    }

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

public sealed class TestMultiplexedConnectionDecorator : IMultiplexedConnection
{
    public TestMultiplexedStreamDecorator LastStream
    {
        get => _lastStream ?? throw new InvalidOperationException("No stream created yet.");
        set => _lastStream = value;
    }

    public TestTransportOperationHelper<MultiplexedTransportOperation> Operations { get; }

    private readonly IMultiplexedConnection _decoratee;
    private TestMultiplexedStreamDecorator? _lastStream;

    public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.AcceptStream, cancellationToken);

        var stream = new TestMultiplexedStreamDecorator(
            await _decoratee.AcceptStreamAsync(cancellationToken),
            Operations.Hold,
            Operations.Fail,
            Operations.FailureException);
        _lastStream = stream;

        // Check again fail/hold condition in case the configuration was changed while AcceptAsync was pending.
        await Operations.CheckAsync(MultiplexedTransportOperation.AcceptStream, cancellationToken);

        return stream;
    }

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

    public async Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.Close, cancellationToken);

        await _decoratee.CloseAsync(closeError, cancellationToken);

        // Release CreateStream and AcceptStream
        Operations.Hold &= ~(MultiplexedTransportOperation.CreateStream | MultiplexedTransportOperation.AcceptStream);
    }

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(MultiplexedTransportOperation.Connect, cancellationToken);
        return await _decoratee.ConnectAsync(cancellationToken);
    }

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
    public TestTransportOperationHelper<MultiplexedTransportOperation> Operations { get; }

    public ulong Id => _decoratee.Id;

    public PipeReader Input => _input ?? throw new InvalidOperationException("No input for unidirectional stream.");

    public bool IsBidirectional => _decoratee.IsBidirectional;

    public bool IsRemote => _decoratee.IsRemote;

    public bool IsStarted => _decoratee.IsStarted;

    public PipeWriter Output => _output ?? throw new InvalidOperationException("No output for unidirectional stream.");

    public Task ReadsClosed => _decoratee.ReadsClosed;

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
    /// <summary>Installs the test Multiplexed transport.</summary>
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
                    holdOperation: clientHoldOperation,
                    failOperation: clientFailOperation,
                    failureException: clientFailureException))
            .AddSingleton<IMultiplexedClientTransport>(provider =>
                provider.GetRequiredService<TestMultiplexedClientTransportDecorator>())
            .AddSingleton(provider =>
                new TestMultiplexedServerTransportDecorator(
                    new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()),
                    holdOperation: serverHoldOperation,
                    failOperation: serverFailOperation,
                    failureException: serverFailureException))
            .AddSingleton<IMultiplexedServerTransport>(provider =>
                provider.GetRequiredService<TestMultiplexedServerTransportDecorator>());
}
