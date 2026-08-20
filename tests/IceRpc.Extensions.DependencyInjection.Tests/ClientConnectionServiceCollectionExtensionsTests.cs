// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Extensions.DependencyInjection.Tests;

public sealed class ClientConnectionServiceCollectionExtensionsTests
{
    /// <summary>Verifies that a <see cref="ClientConnection" /> registered with a server address URI can be resolved.
    /// </summary>
    [Test]
    public async Task Add_client_connection_with_server_address_uri()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTransport()
            .AddSlicTransport()
            .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
            .BuildServiceProvider(validateScopes: true);

        // Constructing the client connection requires a server address, so a successful resolution verifies the
        // server address URI was applied.
        Assert.That(() => provider.GetRequiredService<ClientConnection>(), Throws.Nothing);
    }
}
