// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the multiplexed transports.</summary>
public abstract partial class MultiplexedTransportConformanceTests
{
    [Test]
    public async Task Call_accept_on_a_listener_accepts_a_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();

        var acceptTask = listener.AcceptAsync(default);
        var clientConnectTask = clientConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () =>
            {
                await using IMultiplexedConnection serverConnection = (await acceptTask).Connection;
                await serverConnection.ConnectAsync(default);
            },
            Throws.Nothing);
        Assert.That(async () => await clientConnectTask, Throws.Nothing);
    }

    [Test]
    public async Task Call_accept_on_a_disposed_listener_fails_with_object_disposed_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await listener.DisposeAsync();

        // Act/Assert
        Assert.That(async () => await listener.AcceptAsync(default), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task Call_accept_on_a_listener_with_a_canceled_cancellation_token_fails_with_operation_canceled_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act
        var acceptTask = listener.AcceptAsync(new CancellationToken(canceled: true));

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task Call_accept_on_a_listener_and_then_dispose_it_fails_with_operation_aborted_error()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        var acceptTask = listener.AcceptAsync(default);

        // Act
        await listener.DisposeAsync();

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await acceptTask);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.OperationAborted));
    }

    [Test]
    public async Task Call_accept_on_a_listener_and_then_cancel_the_cancellation_source_fails_with_operation_canceled_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        using var cancellationSource = new CancellationTokenSource();

        var acceptTask = listener.AcceptAsync(cancellationSource.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // Give a few ms for accept to start

        // Act
        cancellationSource.Cancel();

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }
}
