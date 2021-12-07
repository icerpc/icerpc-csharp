// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Slice.NamespaceMD.M1.M2.M3;
using IceRpc.Tests.Slice.NamespaceMD.WithNamespace;
using IceRpc.Tests.Slice.NamespaceMD.WithNamespace.N1.N2;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(10000)]
    public sealed class NamespaceMetadataTests : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly Proxy _prx;

        public NamespaceMetadataTests()
        {
            _serviceProvider = new IntegrationServiceCollection()
                .AddTransient<IDispatcher, NamespaceMDOperations>()
                .BuildServiceProvider();
            _prx = NamespaceMDOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>()).Proxy;
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task NamespaceMetadata_Definitions()
        {
            var prx = new NamespaceMDOperationsPrx(_prx.Clone());

            C1 c1 = await prx.GetWithNamespaceC2AsC1Async();
            Assert.That(c1, Is.Not.Null);
            Assert.That(c1, Is.InstanceOf<C2>());
            Assert.DoesNotThrowAsync(async () => await prx.GetWithNamespaceC2AsC2Async());
            Assert.DoesNotThrowAsync(async () => await prx.GetWithNamespaceN1N2S1Async());
            Assert.DoesNotThrowAsync(async () => await prx.GetNestedM0M2M3S2Async());
            Assert.ThrowsAsync<E1>(async () => await prx.ThrowWithNamespaceE1Async());

            Assert.AreEqual(Encoding.Ice20, prx.Proxy.Encoding);
            Assert.ThrowsAsync<E1>(async () => await prx.ThrowWithNamespaceE2Async());

            prx.Proxy.Encoding = Encoding.Ice11;
            Assert.ThrowsAsync<E2>(async () => await prx.ThrowWithNamespaceE2Async());
        }
    }

    public class NamespaceMDOperations : Service, INamespaceMDOperations
    {
        public ValueTask<S2> GetNestedM0M2M3S2Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new S2());

        public ValueTask<C1> GetWithNamespaceC2AsC1Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new C2());

        public ValueTask<C2> GetWithNamespaceC2AsC2Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new C2());

        public ValueTask<S1> GetWithNamespaceN1N2S1Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new S1());

        public ValueTask ThrowWithNamespaceE1Async(Dispatch dispatch, CancellationToken cancel) =>
            throw new E1();
        public ValueTask ThrowWithNamespaceE2Async(Dispatch dispatch, CancellationToken cancel) =>
            throw new E2();
    }
}
