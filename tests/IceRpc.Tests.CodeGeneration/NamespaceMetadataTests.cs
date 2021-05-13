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
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly INamespaceMDOperationsPrx _prx;

        public NamespaceMetadataTests()
        {
            _server = new Server
            {
                Dispatcher = new NamespaceMDOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()

            };
            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
            // TODO: temporary
            _connection.ConnectAsync().Wait();
            _prx = INamespaceMDOperationsPrx.FromConnection(_connection);
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
            await _connection.ShutdownAsync();
        }
    }

    public class NamespaceMDOperations : INamespaceMDOperations
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
