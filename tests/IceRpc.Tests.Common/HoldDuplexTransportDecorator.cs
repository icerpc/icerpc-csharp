// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

/// <summary>A decorator for duplex server transport that holds any ConnectAsync and ShutdownAsync for connections
/// accepted by this transport.</summary>

public class HoldDuplexServerTransportDecorator : IDuplexServerTransport
{
    public string Name => _decoratee.Name;

    private readonly IDuplexServerTransport _decoratee;
    private readonly TaskCompletionSource _holdConnectTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _holdShutdownTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HoldDuplexServerTransportDecorator(IDuplexServerTransport decoratee) => _decoratee = decoratee;

    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions) =>
        new HoldListenerDecorator(
            _decoratee.Listen(serverAddress, options, serverAuthenticationOptions),
            _holdConnectTcs.Task,
            _holdShutdownTcs.Task);

    public void Release()
    {
        ReleaseConnect();
        ReleaseShutdown();
    }

    public void ReleaseConnect() => _holdConnectTcs.TrySetResult();

    public void ReleaseShutdown() => _holdShutdownTcs.TrySetResult();

    private class HoldListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IDuplexConnection> _decoratee;
        private readonly Task _holdConnectTask;
        private readonly Task _holdShutdownTask;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return (new HoldConnection(connection, _holdConnectTask, _holdShutdownTask), remoteNetworkAddress);
        }
        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal HoldListenerDecorator(
            IListener<IDuplexConnection> decoratee,
            Task holdConnectTask,
            Task holdShutdownTask)
        {
            _decoratee = decoratee;
            _holdConnectTask = holdConnectTask;
            _holdShutdownTask = holdShutdownTask;
        }
    }

    private class HoldConnection : IDuplexConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IDuplexConnection _decoratee;
        private readonly Task _holdConnectTask;
        private readonly Task _holdShutdownTask;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            await _holdConnectTask.WaitAsync(cancellationToken);
            return await _decoratee.ConnectAsync(cancellationToken);
        }

        public void Dispose() => _decoratee.Dispose();

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            _decoratee.ReadAsync(buffer, cancellationToken);

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await _holdShutdownTask.WaitAsync(cancellationToken);
            await _decoratee.ShutdownAsync(cancellationToken);
        }

        public ValueTask WriteAsync(
            IReadOnlyList<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancellationToken) => _decoratee.WriteAsync(buffers, cancellationToken);

        internal HoldConnection(IDuplexConnection decoratee, Task holdConnectTask, Task holdShutdownTask)
        {
            _decoratee = decoratee;
            _holdConnectTask = holdConnectTask;
            _holdShutdownTask = holdShutdownTask;
        }
    }
}
