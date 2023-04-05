// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

[Parallelizable(ParallelScope.All)]
public class IceObjectTests
{
    /// <summary>Verifies the operations of <see cref="IIceObject" />.</summary>
    [Test]
    public async Task Ice_operations([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(Protocol.Parse(protocol), new PingableService())
            .AddIceRpcProxy<IIceObject, IceObjectProxy>(new Uri($"{protocol}:/service"))
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

    [Test]
    public async Task Downcast_proxy_with_as_async_succeeds()
    {
        // Arrange
        var proxy = new MyBaseInterfaceProxy(new ColocInvoker(new MyDerivedInterfaceService()));

        // Act
        MyDerivedInterfaceProxy? derived = await proxy.IceAsAsync<MyDerivedInterfaceProxy>();

        // Assert
        Assert.That(derived, Is.Not.Null);
    }

    [Test]
    public async Task Downcast_proxy_with_as_async_fails()
    {
        // Arrange
        var proxy = new MyBaseInterfaceProxy(new ColocInvoker(new MyBaseInterfaceService()));

        // Act
        MyDerivedInterfaceProxy? derived = await proxy.IceAsAsync<MyDerivedInterfaceProxy>();

        // Assert
        Assert.That(derived, Is.Null);
    }

    private class PingableService : IceObjectService, IPingableService
    {
        public ValueTask PingAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
    }

    private class MyBaseInterfaceService : IceObjectService, IMyBaseInterfaceService
    {
    }

    private sealed class MyDerivedInterfaceService : MyBaseInterfaceService, IMyDerivedInterfaceService
    {
    }
}
