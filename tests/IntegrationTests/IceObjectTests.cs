// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Slice.Ice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

[Parallelizable(ParallelScope.All)]
public partial class IceObjectTests
{
    /// <summary>Verifies the operations of <see cref="IIceObject" />.</summary>
    [Test]
    public async Task Ice_operations([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(Protocol.Parse(protocol), new PingableService())
            .AddSingleton<IIceObject>(
                provider => provider.CreateSliceProxy<IceObjectProxy>(new Uri($"{protocol}:/service")))
            .BuildServiceProvider(validateScopes: true);
        IIceObject proxy = provider.GetRequiredService<IIceObject>();
        Server server = provider.GetRequiredService<Server>();
        server.Listen();

        string[] ids = new string[]
        {
            "::Ice::Object", "::IceRpc::IntegrationTests::Pingable"
        };

        Assert.That(await proxy.IceIdsAsync(), Is.EqualTo(ids));
        Assert.That(await proxy.IceIsAAsync("::Ice::Object"), Is.True);
        Assert.That(await proxy.IceIsAAsync("::IceRpc::IntegrationTests::Pingable"), Is.True);
        Assert.That(await proxy.IceIsAAsync("::Foo"), Is.False);
        Assert.DoesNotThrowAsync(() => proxy.IcePingAsync());
    }

    [SliceService]
    private partial class PingableService : IPingableService
    {
        public ValueTask PingAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
    }
}
