// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IceRpc.Extensions.DependencyInjection.Tests;

public sealed class ClientConnectionServiceCollectionExtensionsTests
{
    /// <summary>Verifies that a <see cref="ClientConnection" /> registered with a server address URI can be resolved
    /// and uses this server address.</summary>
    [Test]
    public async Task Add_client_connection_with_server_address_uri()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTransport()
            .AddSlicTransport()
            .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
            .BuildServiceProvider(validateScopes: true);

        Assert.That(() => provider.GetRequiredService<ClientConnection>(), Throws.Nothing);
        Assert.That(
            provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value.ServerAddress,
            Is.EqualTo(new ServerAddress(new Uri("icerpc://localhost"))));
    }
}
