// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

[Parallelizable(ParallelScope.All)]
public class ServiceTests
{
    /// <summary>Verifies the operations of <see cref="Service"/>.</summary>
    [Test]
    public async Task Service_operations([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new Service(), Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);
        var service = ServicePrx.FromConnection(provider.GetRequiredService<ClientConnection>(), "/service");
        var server = provider.GetRequiredService<Server>();
        server.Listen();

        string[] ids = new string[]
        {
            "::Slice::Service",
        };

        Assert.That(await service.IceIdsAsync(), Is.EqualTo(ids));
        Assert.That(await service.IceIsAAsync("::Slice::Service"), Is.True);
        Assert.That(await service.IceIsAAsync("::Foo"), Is.False);
        Assert.DoesNotThrowAsync(() => service.IcePingAsync());
    }
}
