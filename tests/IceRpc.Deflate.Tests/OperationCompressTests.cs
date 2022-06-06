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
        var pipeline = new Pipeline();
        pipeline.UseDeflate();
        bool compressRequestFeature = false;
        bool compressResponseFeature = false;
        pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
        {
            var response = await next.InvokeAsync(request, cancel);
            compressRequestFeature =
                request.Features.Get<ICompressFeature>() is ICompressFeature compress && compress.Value;
            return response;
        }));

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
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var prx = MyOperationsAPrx.FromConnection(provider.GetRequiredService<ClientConnection>(), "/", pipeline);

        // Act
        int r = await prx.OpWithCompressArgsAndReturnAttributeAsync(10);

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
