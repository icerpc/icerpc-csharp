// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Compressor.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class OperationGeneratedCodeTests
{
    [Test]
    public async Task Operation_with_compress_args_and_return_attribute(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        bool compressRequestFeature = false;
        bool compressResponseFeature = false;

        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<MyOperationsAService>()
            .AddClientServerColocTest(Protocol.IceRpc)
            .AddIceRpcServer(builder =>
            {
                builder.UseCompressor(compressionFormat);
                builder.Use(next => new InlineDispatcher(async (request, cancellationToken) =>
                {
                    var response = await next.DispatchAsync(request, cancellationToken);
                    compressResponseFeature =
                        request.Features.Get<ICompressFeature>() is ICompressFeature compress && compress.Value;
                    return response;
                }));
                builder.Map<MyOperationsAService>("/");
            })
            .AddIceRpcInvoker(
                builder => builder
                    .UseCompressor(compressionFormat)
                    .Use(next => new InlineInvoker(async (request, cancellationToken) =>
                    {
                        IncomingResponse response = await next.InvokeAsync(request, cancellationToken);
                        compressRequestFeature =
                            request.Features.Get<ICompressFeature>() is ICompressFeature compress && compress.Value;
                        return response;
                    }))
                .Into<ClientConnection>())
            .AddSingleton<IMyOperationsA>(
                provider => provider.CreateSliceProxy<MyOperationsAProxy>(new Uri("icerpc:/")))
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        IMyOperationsA proxy = provider.GetRequiredService<IMyOperationsA>();

        // Act
        int r = await proxy.OpWithCompressArgsAndReturnAttributeAsync(10);

        // Assert
        Assert.That(r, Is.EqualTo(10));
        Assert.That(compressRequestFeature, Is.True);
        Assert.That(compressResponseFeature, Is.True);
    }

    [SliceService]
    public partial class MyOperationsAService : IMyOperationsAService
    {
        public ValueTask<int> OpWithCompressArgsAndReturnAttributeAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);
    }
}
