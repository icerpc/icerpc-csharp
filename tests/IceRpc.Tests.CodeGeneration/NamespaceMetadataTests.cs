// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.CodeGeneration.NamespaceMD.M1.M2.M3;
using IceRpc.Tests.CodeGeneration.NamespaceMD.WithNamespace;
using IceRpc.Tests.CodeGeneration.NamespaceMD.WithNamespace.N1.N2;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(10000)]
    public class NamespaceMetadataTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly INamespaceMDOperationsPrx _prx;

        public NamespaceMetadataTests()
        {
            _communicator = new Communicator();
            _server = new Server(_communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.Communicator
                });
            _prx = _server.Add("/test", new NamespaceMDOperations(), INamespaceMDOperationsPrx.Factory);
        }

        [Test]
        public async Task NamespaceMetadata_Definitions()
        {
            C1 c1 = await _prx.GetWithNamespaceC2AsC1Async();
            Assert.IsNotNull(c1);
            Assert.IsInstanceOf<C2>(c1);
            Assert.DoesNotThrowAsync(async () => await _prx.GetWithNamespaceC2AsC2Async());
            Assert.DoesNotThrowAsync(async () => await _prx.GetWithNamespaceN1N2S1Async());
            Assert.DoesNotThrowAsync(async () => await _prx.GetNestedM0M2M3S2Async());
            Assert.ThrowsAsync<E1>(async () => await _prx.ThrowWithNamespaceE1Async());
            Assert.ThrowsAsync<E2>(async () => await _prx.ThrowWithNamespaceE2Async());
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }
    }

    public class NamespaceMDOperations : IAsyncNamespaceMDOperations
    {
        public ValueTask<S2> GetNestedM0M2M3S2Async(Current current, CancellationToken cancel) =>
            new(new S2());

        public ValueTask<C1> GetWithNamespaceC2AsC1Async(Current current, CancellationToken cancel) =>
            new(new C2());

        public ValueTask<C2> GetWithNamespaceC2AsC2Async(Current current, CancellationToken cancel) =>
            new(new C2());

        public ValueTask<S1> GetWithNamespaceN1N2S1Async(Current current, CancellationToken cancel) =>
            new(new S1());

        public ValueTask ThrowWithNamespaceE1Async(Current current, CancellationToken cancel) =>
            throw new E1();
        public ValueTask ThrowWithNamespaceE2Async(Current current, CancellationToken cancel) =>
            throw new E2();
    }
}
