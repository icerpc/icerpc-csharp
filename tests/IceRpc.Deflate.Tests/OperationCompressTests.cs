// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Builder;
using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Deflate.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class OperationGeneratedCodeTests
{
    [Test]
    public async Task Operation_with_compress_args_and_return_attribute()
    {
        // Arrange
        bool compressRequestFeature = false;
        bool compressResponseFeature = false;

        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<MyOperationsA>()
            .AddColocTest(builder =>
            {
                builder.UseDeflate();
                builder.Use(next => new InlineDispatcher(async (request, cancel) =>
                {
                    var response = await next.DispatchAsync(request, cancel);
                    compressResponseFeature =
                        request.Features.Get<ICompressFeature>() is ICompressFeature compress && compress.Value;
                    return response;
                }));
                builder.Map<MyOperationsA>("/");
            })
            .AddIceRpcInvoker(builder => builder
                .UseDeflate()
                .Use(next => new InlineInvoker(async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    compressRequestFeature =
                        request.Features.Get<ICompressFeature>() is ICompressFeature compress && compress.Value;
                    return response;
                }))
                .Into<ClientConnection>())
            .AddIceRpcProxy<IMyOperationsAProxy, MyOperationsAProxy>("icerpc:/")
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        IMyOperationsAProxy proxy = provider.GetRequiredService<IMyOperationsAProxy>();

        // Act
        int r = await proxy.OpWithCompressArgsAndReturnAttributeAsync(10);

        // Assert
        Assert.That(r, Is.EqualTo(10));
        Assert.That(compressRequestFeature, Is.True);
        Assert.That(compressResponseFeature, Is.True);
    }

    public class MyOperationsA : Service, IMyOperationsA
    {
        public ValueTask<int> OpWithCompressArgsAndReturnAttributeAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancel) => new(p);
    }
}
