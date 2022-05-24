// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

[Parallelizable(ParallelScope.All)]
[Timeout(5000)]
public class ServiceTests
{
    /// <summary>Verifies the operations of <see cref="Service"/>.</summary>
    [Test]
    public async Task Service_operations([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new Service())
            .UseProtocol(protocol)
            .BuildServiceProvider();

        var service = ServicePrx.FromConnection(provider.GetRequiredService<ClientConnection>(), "/service");

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
