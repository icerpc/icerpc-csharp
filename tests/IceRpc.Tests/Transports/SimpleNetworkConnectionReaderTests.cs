// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class SimpleNetworkConnectionReaderTests
{
    // TODO: Add more tests

    /// <summary>Verifies that reading from the connection updates its last activity property.</summary>
    [Test]
    public async Task Read_calls_OnRead()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .UseSimpleTransport("icerpc://colochost/")
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
        var clientConnection = provider.GetRequiredService<ISimpleNetworkConnection>();
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        Task<NetworkConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        using ISimpleNetworkConnection serverConnection = await acceptTask;
        Task<NetworkConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);

        using var reader = new SimpleNetworkConnectionReader(
            clientConnection,
            MemoryPool<byte>.Shared,
            4096);

        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);

        bool onReadCalled = false;
        reader.OnRead = () => onReadCalled = true;

        // Act
        await reader.ReadAsync(default);

        // Assert
        Assert.That(onReadCalled, Is.True);
    }
}
