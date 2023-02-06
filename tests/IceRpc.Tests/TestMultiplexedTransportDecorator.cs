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
    AcceptStream = 1,
    CreateStream = 2,
    Connect = 4,
    Close = 8,
    DisposeAsync = 16,
    StreamRead = 32,
    StreamWrite = 64,
}

#pragma warning disable CA1001 // _lastConnection is disposed by the caller.
public sealed class TestMultiplexedClientTransportDecorator : IMultiplexedClientTransport
#pragma warning restore CA1001
{
    public MultiplexedTransportOperation FailOperation { get; set; }
    public Exception FailureException { get; set; } = new Exception();
    public MultiplexedTransportOperation HoldOperation { get; set; }
    public TestMultiplexedConnectionDecorator LastConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    private readonly IMultiplexedClientTransport _decoratee;
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
            clientAuthenticationOptions))
            {
                HoldOperation = HoldOperation,
                FailOperation = FailOperation,
                FailureException = FailureException
            };
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
        HoldOperation = holdOperation;
        FailOperation = failOperation;
        FailureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
    }
}

/// <summary>A decorator for Multiplexed server transport that holds any ConnectAsync and ShutdownAsync for connections
/// accepted by this transport.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestMultiplexedServerTransportDecorator : IMultiplexedServerTransport
#pragma warning restore CA1001
{
    public MultiplexedTransportOperation FailOperation
    {
        get => _listener?.FailOperation ?? _failOperation;

        set
        {
            if (_listener is null)
            {
                _failOperation = value;
            }
            else
            {
                _listener.FailOperation = value;
            }
        }
    }

    public Exception FailureException
    {
        get => _listener?.FailureException ?? _failureException;

        set
        {
            if (_listener is null)
            {
                _failureException = value;
            }
            else
            {
                _listener.FailureException = value;
            }
        }
    }

    public MultiplexedTransportOperation HoldOperation
    {
        get => _listener?.HoldOperation ?? _holdOperation;

        set
        {
            if (_listener is null)
            {
                _holdOperation = value;
            }
            else
            {
                _listener.HoldOperation = value;
            }
        }
    }

    public string Name => _decoratee.Name;

    public TestMultiplexedConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    private readonly IMultiplexedServerTransport _decoratee;
    private MultiplexedTransportOperation _failOperation;
    private Exception _failureException;
    private MultiplexedTransportOperation _holdOperation;
    private TestMultiplexedListenerDecorator? _listener;

    public TestMultiplexedServerTransportDecorator(
        IMultiplexedServerTransport decoratee,
        MultiplexedTransportOperation holdOperation = MultiplexedTransportOperation.None,
        MultiplexedTransportOperation failOperation = MultiplexedTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        _holdOperation = holdOperation;
        _failOperation = failOperation;
        _failureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
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
            _decoratee.Listen(serverAddress, options, serverAuthenticationOptions))
            {
                HoldOperation = _holdOperation,
                FailOperation = _failOperation,
                FailureException = _failureException
            };
        return _listener;
    }

    private class TestMultiplexedListenerDecorator : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal MultiplexedTransportOperation FailOperation { get; set; }
        internal Exception FailureException { get; set; } = new Exception();
        internal MultiplexedTransportOperation HoldOperation { get; set; }

        internal TestMultiplexedConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IMultiplexedConnection> _decoratee;
        private TestMultiplexedConnectionDecorator? _lastAcceptedConnection;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IMultiplexedConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

            var testConnection = new TestMultiplexedConnectionDecorator(connection)
                {
                    HoldOperation = HoldOperation,
                    FailOperation = FailOperation,
                    FailureException = FailureException
                };
            LastAcceptedConnection = testConnection;
            return (testConnection, remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal TestMultiplexedListenerDecorator(IListener<IMultiplexedConnection> decoratee) =>
            _decoratee = decoratee;
    }
}

public sealed class TestMultiplexedConnectionDecorator : IMultiplexedConnection
{
    public TestMultiplexedStreamDecorator LastStream
    {
        get => _lastStream ?? throw new InvalidOperationException("No stream created yet.");
        set => _lastStream = value;
    }

    public Task DisposeCalled => _disposeCalledTcs.Task;

    public MultiplexedTransportOperation FailOperation { get; set; }

    public Exception FailureException { get; set; } = new Exception();

    public MultiplexedTransportOperation HoldOperation
    {
        get => _holdOperation;

        set
        {
            _holdOperation = value;

            if (_holdOperation.HasFlag(MultiplexedTransportOperation.CreateStream))
            {
                _holdCreateStreamTcs = new();
            }
            else
            {
                _holdCreateStreamTcs.TrySetResult();
            }

            if (_holdOperation.HasFlag(MultiplexedTransportOperation.AcceptStream))
            {
                _holdAcceptStreamTcs = new();
            }
            else
            {
                _holdAcceptStreamTcs.TrySetResult();
            }

            if (_holdOperation.HasFlag(MultiplexedTransportOperation.Connect))
            {
                _holdConnectTcs = new();
            }
            else
            {
                _holdConnectTcs.TrySetResult();
            }

            if (_holdOperation.HasFlag(MultiplexedTransportOperation.Close))
            {
                _holdCloseTcs = new();
            }
            else
            {
                _holdCloseTcs.TrySetResult();
            }

            if (_holdOperation.HasFlag(MultiplexedTransportOperation.DisposeAsync))
            {
                _holdDisposeTcs = new();
            }
            else
            {
                _holdDisposeTcs.TrySetResult();
            }
        }
    }

    private readonly IMultiplexedConnection _decoratee;
    private readonly TaskCompletionSource _disposeCalledTcs = new();
    private TaskCompletionSource _holdAcceptStreamTcs = new();
    private TaskCompletionSource _holdCloseTcs = new();
    private TaskCompletionSource _holdConnectTcs = new();
    private TaskCompletionSource _holdCreateStreamTcs = new();
    private TaskCompletionSource _holdDisposeTcs = new();
    private MultiplexedTransportOperation _holdOperation;
    private TestMultiplexedStreamDecorator? _lastStream;

    public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken)
    {
        await CheckFailAndHoldAsync();

        var stream = new TestMultiplexedStreamDecorator(await _decoratee.AcceptStreamAsync(cancellationToken))
            {
                HoldOperation = HoldOperation,
                FailOperation = FailOperation,
                FailureException = FailureException
             };
        _lastStream = stream;

        // Check again fail/hold condition in case the configuration was changed while AcceptAsync was pending.
        await CheckFailAndHoldAsync();

        return stream;

        async Task CheckFailAndHoldAsync()
        {
            if (FailOperation.HasFlag(MultiplexedTransportOperation.AcceptStream))
            {
                throw FailureException;
            }
            await _holdAcceptStreamTcs.Task.WaitAsync(cancellationToken);
        }
    }

    public async ValueTask<IMultiplexedStream> CreateStreamAsync(
        bool bidirectional,
        CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(MultiplexedTransportOperation.CreateStream))
        {
            throw FailureException;
        }
        await _holdCreateStreamTcs.Task.WaitAsync(cancellationToken);

        var stream = new TestMultiplexedStreamDecorator(
            await _decoratee.CreateStreamAsync(bidirectional, cancellationToken))
            {
                HoldOperation = HoldOperation,
                FailOperation = FailOperation,
                FailureException = FailureException
            };
        _lastStream = stream;
        return stream;
    }

    public async Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(MultiplexedTransportOperation.Close))
        {
            throw FailureException;
        }
        await _holdCloseTcs.Task.WaitAsync(cancellationToken);

        await _decoratee.CloseAsync(closeError, cancellationToken);

        _holdAcceptStreamTcs.TrySetResult();
        _holdCreateStreamTcs.TrySetResult();
    }

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(MultiplexedTransportOperation.Connect))
        {
            throw FailureException;
        }
        await _holdConnectTcs.Task.WaitAsync(cancellationToken);

        return await _decoratee.ConnectAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCalledTcs.TrySetResult();
        await _holdDisposeTcs.Task;

        _holdAcceptStreamTcs.TrySetResult();
        _holdConnectTcs.TrySetResult();
        _holdCloseTcs.TrySetResult();
        _holdCreateStreamTcs.TrySetResult();

        await _decoratee.DisposeAsync();
    }

    internal TestMultiplexedConnectionDecorator(IMultiplexedConnection decoratee) => _decoratee = decoratee;
}

public sealed class TestMultiplexedStreamDecorator : IMultiplexedStream
{
    public MultiplexedTransportOperation FailOperation
    {
        get => _failOperation;

        set
        {
            _failOperation = value;
            if (_output is not null)
            {
                _output.FailWriteOperation = _failOperation.HasFlag(MultiplexedTransportOperation.StreamWrite);
            }
            if (_input is not null)
            {
                _input.FailReadOperation = _failOperation.HasFlag(MultiplexedTransportOperation.StreamRead);
            }
        }
    }

    public Exception FailureException
    {
        get => _failureException;

        set
        {
            _failureException = value;
            if (_output is not null)
            {
                _output.FailureException = value;
            }
            if (_input is not null)
            {
                _input.FailureException = value;
            }
        }
    }

    public MultiplexedTransportOperation HoldOperation
    {
        get => _holdOperation;

        set
        {
            _holdOperation = value;
            if (_output is not null)
            {
                _output.HoldWriteOperation = _holdOperation.HasFlag(MultiplexedTransportOperation.StreamWrite);
            }
            if (_input is not null)
            {
                _input.HoldReadOperation = _holdOperation.HasFlag(MultiplexedTransportOperation.StreamRead);
            }
        }
    }

    public ulong Id => _decoratee.Id;

    public PipeReader Input => _input ?? throw new InvalidOperationException("No input for unidirectional stream.");

    public bool IsBidirectional => _decoratee.IsBidirectional;

    public bool IsRemote => _decoratee.IsRemote;

    public bool IsStarted => _decoratee.IsStarted;

    public PipeWriter Output => _output ?? throw new InvalidOperationException("No output for unidirectional stream.");

    public Task ReadsClosed => _decoratee.ReadsClosed;

    public Task WritesClosed => _decoratee.WritesClosed;

    private readonly IMultiplexedStream _decoratee;
    private Exception _failureException = new();
    private MultiplexedTransportOperation _failOperation;
    private MultiplexedTransportOperation _holdOperation;
    private readonly TestPipeReader? _input;
    private readonly TestPipeWriter? _output;

    internal TestMultiplexedStreamDecorator(IMultiplexedStream decoratee)
    {
        _decoratee = decoratee;
        if (!IsRemote || IsBidirectional)
        {
            _output = new TestPipeWriter(decoratee.Output)
                {
                    FailWriteOperation = _failOperation.HasFlag(MultiplexedTransportOperation.StreamWrite),
                    FailureException = _failureException,
                    HoldWriteOperation = _holdOperation.HasFlag(MultiplexedTransportOperation.StreamWrite)
                };
        }
        if (IsRemote || IsBidirectional)
        {
            _input = new TestPipeReader(decoratee.Input)
                {
                    FailReadOperation = _failOperation.HasFlag(MultiplexedTransportOperation.StreamRead),
                    FailureException = _failureException,
                    HoldReadOperation = _holdOperation.HasFlag(MultiplexedTransportOperation.StreamRead)
                };
        }
    }
}

internal sealed class TestPipeWriter : PipeWriter
{
    internal bool FailWriteOperation { get; set; }

    internal Exception FailureException { get; set; } = new Exception();

    internal bool HoldWriteOperation
    {
        get => _holdWriteOperation;

        set
        {
            _holdWriteOperation = value;
            if (_holdWriteOperation)
            {
                _holdWriteTcs = new();
            }
            else
            {
                _holdWriteTcs.TrySetResult();
            }
        }
    }

    private readonly PipeWriter _decoratee;

    private bool _holdWriteOperation;
    private TaskCompletionSource _holdWriteTcs = new();

    public override void Advance(int bytes) => _decoratee.Advance(bytes);

    public override void CancelPendingFlush() => _decoratee.CancelPendingFlush();

    public override void Complete(Exception? exception)
    {
        _holdWriteTcs.TrySetResult();
        _decoratee.Complete(exception);
    }

    public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
    {
        if (FailWriteOperation)
        {
            throw FailureException;
        }
        await _holdWriteTcs.Task.WaitAsync(cancellationToken);
        return await _decoratee.FlushAsync(cancellationToken);
    }

    public override Memory<byte> GetMemory(int sizeHint = 0) => _decoratee.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint = 0) => _decoratee.GetSpan(sizeHint);

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken)
    {
        if (FailWriteOperation)
        {
            throw FailureException;
        }
        await _holdWriteTcs.Task.WaitAsync(cancellationToken);
        return await _decoratee.WriteAsync(source, cancellationToken);
    }

    internal TestPipeWriter(PipeWriter decoratee) =>_decoratee = decoratee;
}

internal sealed class TestPipeReader : PipeReader
{
    internal bool FailReadOperation { get; set; }

    internal Exception FailureException { get; set; } = new Exception();

    internal bool HoldReadOperation
    {
        get => _holdReadOperation;

        set
        {
            _holdReadOperation = value;
            if (_holdReadOperation)
            {
                _holdReadTcs = new();
            }
            else
            {
                _holdReadTcs.TrySetResult();
            }
        }
    }

    private readonly PipeReader _decoratee;
    private bool _holdReadOperation;
    private TaskCompletionSource _holdReadTcs = new();

    public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _decoratee.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _decoratee.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        _holdReadTcs.TrySetResult();
        _decoratee.Complete(exception);
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (FailReadOperation)
        {
            throw FailureException;
        }
        await _holdReadTcs.Task.WaitAsync(cancellationToken);

        ReadResult result = await _decoratee.ReadAsync(cancellationToken);

        // Check again fail/hold condition in case the configuration was changed while ReadAsync was pending.
        if (FailReadOperation)
        {
            throw FailureException;
        }
        await _holdReadTcs.Task.WaitAsync(cancellationToken);
        return result;
    }

    public override bool TryRead(out ReadResult result)
    {
        result = new ReadResult();
        return false;
    }

    internal TestPipeReader(PipeReader decoratee) => _decoratee = decoratee;
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
