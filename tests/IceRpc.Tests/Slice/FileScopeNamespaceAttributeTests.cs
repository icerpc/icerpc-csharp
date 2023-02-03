// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice.FileScopeNamespaceAttribute.WithFileScopeNamespace;

[Parallelizable(scope: ParallelScope.All)]
public class FileScopeNamespaceAttributeTests
{
    public class FileScopeNamespaceOperations : Service, IFileScopeNamespaceOperations
    {
        public ValueTask<S1> Op1Async(S1 p, IFeatureCollection features, CancellationToken cancellationToken) =>
            new(p);
    }

    [Test]
    public async Task Operation_with_types_using_cs_namespace_attribute()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(dispatcher: new FileScopeNamespaceOperations())
            .AddIceRpcProxy<IFileScopeNamespaceOperationsProxy, FileScopeNamespaceOperationsProxy>()
            .BuildServiceProvider(validateScopes: true);

        IFileScopeNamespaceOperationsProxy proxy = provider.GetRequiredService<IFileScopeNamespaceOperationsProxy>();
        provider.GetRequiredService<Server>().Listen();

        S1 r = await proxy.Op1Async(new S1("10"));

        Assert.That(r.I, Is.EqualTo("10"));
    }
}
