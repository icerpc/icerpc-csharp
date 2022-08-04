// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
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
            .AddIceRpcProxy<IServiceProxy, ServiceProxy>(new Uri($"{protocol}:/service"))
            .BuildServiceProvider(validateScopes: true);
        IServiceProxy proxy = provider.GetRequiredService<IServiceProxy>();
        Server server = provider.GetRequiredService<Server>();
        server.Listen();

        string[] ids = new string[]
        {
            "::IceRpc::Slice::Service",
        };

        Assert.Multiple(async () =>
        {
            Assert.That(await proxy.IceIdsAsync(), Is.EqualTo(ids));
            Assert.That(await proxy.IceIsAAsync("::IceRpc::Slice::Service"), Is.True);
            Assert.That(await proxy.IceIsAAsync("::Foo"), Is.False);
            Assert.DoesNotThrowAsync(() => proxy.IcePingAsync());
        });
    }

    /// <summary>Verifies the operations of <see cref="Service"/> can be overridden.</summary>
    [Test]
    public async Task Service_override_operations([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        var testService = new TestService();
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(testService, Protocol.FromString(protocol))
            .AddIceRpcProxy<IServiceProxy, ServiceProxy>(new Uri($"{protocol}:/service"))
            .BuildServiceProvider(validateScopes: true);
        IServiceProxy proxy = provider.GetRequiredService<IServiceProxy>();
        Server server = provider.GetRequiredService<Server>();
        server.Listen();

        // Act
        await proxy.IceIsAAsync("::IceRpc::Slice::Service");
        await proxy.IceIdsAsync();
        await proxy.IcePingAsync();

        Assert.Multiple(() =>
        {
            Assert.That(testService.OverriddenIceIdsCalled, Is.True);
            Assert.That(testService.OverriddenIceIsACalled, Is.True);
            Assert.That(testService.OverriddenPingCalled, Is.True);
        });
    }

    internal class TestService : Service, IDispatcher
    {
        internal bool OverriddenPingCalled;
        internal bool OverriddenIceIdsCalled;
        internal bool OverriddenIceIsACalled;

        public override ValueTask IcePingAsync(IFeatureCollection features, CancellationToken cancel)
        {
            OverriddenPingCalled = true;
            return base.IcePingAsync(features, cancel);
        }

        public override ValueTask<bool> IceIsAAsync(string id, IFeatureCollection features, CancellationToken cancel)
        {
            OverriddenIceIsACalled = true;
            return base.IceIsAAsync(id, features, cancel);
        }

        public override ValueTask<IEnumerable<string>> IceIdsAsync(IFeatureCollection features, CancellationToken cancel)
        {
            OverriddenIceIdsCalled = true;
            return base.IceIdsAsync(features, cancel);
        }
    }
}
