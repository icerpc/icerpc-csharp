// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

/// <summary>This enumeration describes the duplex transport operations. It's used as a set of flags to specify an
/// enumeration value for multiple operations.</summary>
[Flags]
public enum DuplexTransportOperation
{
    /// <summary>The no-operation enumerator value.</summary>
    None = 0,
    /// <summary>The <see cref="IListener{IDuplexConnection}.AcceptAsync" /> operation.</summary>
    Accept = 1,
    /// <summary>The <see cref="IDuplexConnection.ConnectAsync" /> operation.</summary>
    Connect = 2,
    /// <summary>The <see cref="IDisposable.Dispose" /> operation.</summary>
    Dispose = 4,
    /// <summary>The <see cref="IDuplexConnection.ReadAsync" /> operation.</summary>
    Read = 8,
    /// <summary>The <see cref="IDuplexConnection.ShutdownAsync" /> operation.</summary>
    Shutdown = 16,
    /// <summary>The <see cref="IDuplexConnection.WriteAsync" /> operation.</summary>
    Write = 32,
}

public sealed class TestDuplexClientTransportDecorator : IDuplexClientTransport
{
    /// <summary>The last created connection.</summary>
    public TestDuplexConnectionDecorator LastCreatedConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    private readonly IDuplexClientTransport _decoratee;
    private readonly DuplexTransportOperation _failOperations;
    private readonly Exception _failureException;
    private readonly DuplexTransportOperation _holdOperations;
    private TestDuplexConnectionDecorator? _lastConnection;

    /// <inheritdoc/>
    public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        var connection = new TestDuplexConnectionDecorator(
            _decoratee.CreateConnection(serverAddress, options, clientAuthenticationOptions),
            _holdOperations,
            _failOperations,
            _failureException);
        _lastConnection = connection;
        return connection;
    }

    /// <summary>Constructs a <see cref="TestDuplexClientTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated client transport.</param>
    /// <param name="holdOperations">The operations to hold for connection created with this transport.</param>
    /// <param name="failOperations">The operations that will fail for connections created with this transport.</param>
    /// <param name="failureException">The exception to raise for operations configured to fail. If not specified, an
    /// <see cref="IceRpcException" /> exception with the <see cref="IceRpcError.IceRpcError" /> error code is
    /// used.</param>
    public TestDuplexClientTransportDecorator(
        IDuplexClientTransport decoratee,
        DuplexTransportOperation holdOperations = DuplexTransportOperation.None,
        DuplexTransportOperation failOperations = DuplexTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        _holdOperations = holdOperations;
        _failOperations = failOperations;
        _failureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
    }
}

/// <summary>A <see cref="IDuplexServerTransport" /> decorator to create decorated <see cref="IDuplexConnection" />
/// server connections. It also provides access to the last accepted connection and allows to configure the behavior of
/// the listener and the operations inherited by connections.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestDuplexServerTransportDecorator : IDuplexServerTransport
#pragma warning restore CA1001
{
    /// <summary>The last accepted connection.</summary>
    public TestDuplexConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    /// <summary>The <see cref="TestTransportOperationHelper{DuplexTransportOperation}" /> used by the server
    /// transport <see cref="IListener{IDuplexConnection}" /> operations and inherited by accepted
    /// connections.</summary>
    public TestTransportOperationHelper<DuplexTransportOperation> Operations;

    private readonly IDuplexServerTransport _decoratee;
    private TestDuplexListenerDecorator? _listener;

    /// <summary>Constructs a <see cref="TestDuplexServerTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated server transport.</param>
    /// <param name="holdOperations">The operations to hold for connection created with this transport.</param>
    /// <param name="failOperations">The operations that will fail for connections created with this transport.</param>
    /// <param name="failureException">The exception to raise for operations configured to fail. If not specified, an
    /// <see cref="IceRpcException" /> exception with the <see cref="IceRpcError.IceRpcError" /> error code is
    /// used.</param>
    public TestDuplexServerTransportDecorator(
        IDuplexServerTransport decoratee,
        DuplexTransportOperation holdOperations = DuplexTransportOperation.None,
        DuplexTransportOperation failOperations = DuplexTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperations, failOperations, failureException);
    }

    /// <inheritdoc/>
    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("Test server transport doesn't support multiple listeners.");
        }

        _listener = new TestDuplexListenerDecorator(
            _decoratee.Listen(serverAddress, options, serverAuthenticationOptions),
            Operations);

        return _listener;
    }

    private class TestDuplexListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal TestDuplexConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IDuplexConnection> _decoratee;
        private TestDuplexConnectionDecorator? _lastAcceptedConnection;
        private readonly TestTransportOperationHelper<DuplexTransportOperation> _operations;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            await _operations.CheckAsync(DuplexTransportOperation.Accept, cancellationToken);

            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _operations.CheckAsync(DuplexTransportOperation.Accept, cancellationToken);
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            LastAcceptedConnection = new TestDuplexConnectionDecorator(
                    connection,
                    _operations.Hold,
                    _operations.Fail,
                    _operations.FailureException);
            return (LastAcceptedConnection, remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal TestDuplexListenerDecorator(
            IListener<IDuplexConnection> decoratee,
            TestTransportOperationHelper<DuplexTransportOperation> operations)
        {
            _decoratee = decoratee;
            _operations = operations;
        }
    }
}

/// <summary>An <see cref="IDuplexConnection" /> decorator to configure the behavior of connection
/// operations.</summary>
public sealed class TestDuplexConnectionDecorator : IDuplexConnection
{
    /// <summary>The <see cref="TestTransportOperationHelper{DuplexTransportOperation}" /> used by this connection
    /// operations.</summary>
    public TestTransportOperationHelper<DuplexTransportOperation> Operations { get; }

    private readonly IDuplexConnection _decoratee;

    /// <inheritdoc/>
    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Connect, cancellationToken);
        return await _decoratee.ConnectAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Operations.Called(DuplexTransportOperation.Dispose);
        _decoratee.Dispose();
        Operations.Complete();
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Read, cancellationToken);

        int count = await _decoratee.ReadAsync(buffer, cancellationToken);

        // Check again fail/hold condition in case the configuration was changed while ReadAsync was pending.
        await Operations.CheckAsync(DuplexTransportOperation.Read, cancellationToken);

        return count;
    }

    /// <inheritdoc/>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Shutdown, cancellationToken);
        await _decoratee.ShutdownAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(
        IReadOnlyList<ReadOnlyMemory<byte>> buffers,
        CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Write, cancellationToken);
        await _decoratee.WriteAsync(buffers, cancellationToken);
    }

    internal TestDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        DuplexTransportOperation holdOperation = DuplexTransportOperation.None,
        DuplexTransportOperation failOperation = DuplexTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperation, failOperation, failureException);
    }
}

public static class TestDuplexTransportServiceCollectionExtensions
{
    /// <summary>Installs the test duplex transport.</summary>
    public static IServiceCollection AddTestDuplexTransport(
        this IServiceCollection services,
        DuplexTransportOperation clientHoldOperation = DuplexTransportOperation.None,
        DuplexTransportOperation clientFailOperation = DuplexTransportOperation.None,
        Exception? clientFailureException = null,
        DuplexTransportOperation serverHoldOperation = DuplexTransportOperation.None,
        DuplexTransportOperation serverFailOperation = DuplexTransportOperation.None,
        Exception? serverFailureException = null) => services
        .AddSingleton<ColocTransportOptions>()
        .AddSingleton(provider => new ColocTransport(provider.GetRequiredService<ColocTransportOptions>()))
        .AddSingleton(provider =>
            new TestDuplexClientTransportDecorator(
                provider.GetRequiredService<ColocTransport>().ClientTransport,
                holdOperations: clientHoldOperation,
                failOperations: clientFailOperation,
                failureException: clientFailureException))
        .AddSingleton<IDuplexClientTransport>(provider =>
            provider.GetRequiredService<TestDuplexClientTransportDecorator>())
        .AddSingleton(provider =>
            new TestDuplexServerTransportDecorator(
                provider.GetRequiredService<ColocTransport>().ServerTransport,
                holdOperations: serverHoldOperation,
                failOperations: serverFailOperation,
                failureException: serverFailureException))
        .AddSingleton<IDuplexServerTransport>(provider =>
            provider.GetRequiredService<TestDuplexServerTransportDecorator>());
}
