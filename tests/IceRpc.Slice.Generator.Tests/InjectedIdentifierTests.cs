// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Slice.Generator.Tests;

[Parallelizable(ParallelScope.All)]
public partial class InjectedIdentifierTests
{
    [Test]
    public async Task Verbatim_features_parameter()
    {
        // Arrange
        var proxy = new InjectedIdentifiersProxy(new ColocInvoker(new TestService()));

        // Act
        int result = await proxy.WithFeaturesAsync(@features: 42, features_: FeatureCollection.Empty);

        // Assert
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Verbatim_cancellation_token_parameter()
    {
        // Arrange
        var proxy = new InjectedIdentifiersProxy(new ColocInvoker(new TestService()));

        // Act
        int result = await proxy.WithCancellationTokenAsync(@cancellationToken: 42, cancellationToken_: default);

        // Assert
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Verbatim_request_encode_options_parameter()
    {
        // Arrange
        var proxy = new InjectedIdentifiersProxy(new ColocInvoker(new TestService()));

        // Act
        int result = await proxy.WithRequestEncodeOptionsAsync(@encodeOptions: 42);

        // Assert
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task Verbatim_response_encode_options_parameter()
    {
        // Arrange
        var proxy = new InjectedIdentifiersProxy(new ColocInvoker(new TestService()));

        // Act
        var result = await proxy.WithResponseEncodeOptionsAsync();

        // Assert
        Assert.That(result.Value, Is.EqualTo(1));
        Assert.That(result.@encodeOptions, Is.EqualTo(42));
    }

    [Test]
    public async Task Verbatim_payload_stream_return()
    {
        // Arrange
        var proxy = new InjectedIdentifiersProxy(new ColocInvoker(new TestService()));

        // Act
        var result = await proxy.WithPayloadStreamAsync();
        using IAsyncStream<int> stream = result.@Payload;
        var values = new List<int>();
        await foreach (int value in stream)
        {
            values.Add(value);
        }

        // Assert
        Assert.That(result.Value, Is.EqualTo(42));
        Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Service]
    private sealed partial class TestService : IInjectedIdentifiersService
    {
        public ValueTask<int> WithFeaturesAsync(
            int @features,
            IFeatureCollection features_,
            CancellationToken cancellationToken) => new(@features);

        public ValueTask<int> WithCancellationTokenAsync(
            int @cancellationToken,
            IFeatureCollection features,
            CancellationToken cancellationToken_) => new(@cancellationToken);

        public ValueTask<int> WithRequestEncodeOptionsAsync(
            int @encodeOptions,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(@encodeOptions);

        public ValueTask<(int, int)> WithResponseEncodeOptionsAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((1, 42));

        public ValueTask<(PipeReader Payload_, IAsyncEnumerable<int> Payload)> WithPayloadStreamAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            PipeReader payload = IInjectedIdentifiersService.Response.EncodeWithPayloadStream(
                42,
                features.Get<ISliceFeature>()?.EncodeOptions);
            return new((payload, GetValuesAsync()));

            static async IAsyncEnumerable<int> GetValuesAsync()
            {
                await Task.Yield();
                yield return 1;
                yield return 2;
                yield return 3;
            }
        }
    }
}
