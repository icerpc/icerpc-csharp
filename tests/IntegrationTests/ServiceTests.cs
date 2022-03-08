// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IntegrationTests;

[Parallelizable(ParallelScope.All)]
[Timeout(5000)]
public class ServiceTests
{
    /// <summary>Verifies the operations of <see cref="IceRpc.Slice.Service"/>.</summary>
    [Test]
    public async Task Service_operations()
    {
        await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
            .AddTransient<IDispatcher, Service>()
            .BuildServiceProvider();

        var service = ServicePrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

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
