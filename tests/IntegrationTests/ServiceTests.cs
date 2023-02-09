// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

[Parallelizable(ParallelScope.All)]
public class ServiceTests
{
    /// <summary>Verifies the operations of <see cref="Service" />.</summary>
    [Test]
    public async Task Service_operations([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(Protocol.Parse(protocol), new Service())
            .AddIceRpcProxy<IService, ServiceProxy>(new Uri($"{protocol}:/service"))
            .BuildServiceProvider(validateScopes: true);
        IService proxy = provider.GetRequiredService<IService>();
        Server server = provider.GetRequiredService<Server>();
        server.Listen();

        string[] ids = new string[]
        {
            "::IceRpc::Slice::Service",
        };

        Assert.That(await proxy.IceIdsAsync(), Is.EqualTo(ids));
        Assert.That(await proxy.IceIsAAsync("::IceRpc::Slice::Service"), Is.True);
        Assert.That(await proxy.IceIsAAsync("::Foo"), Is.False);
        Assert.DoesNotThrowAsync(() => proxy.IcePingAsync());
    }
}
