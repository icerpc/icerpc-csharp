// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Locator.Tests;

public class LocatorInterceptorTests
{
    /// <summary>Verifies that the location resolver is not called when the request carries a server address.</summary>
    [Test]
    public async Task Location_resolver_not_called_if_the_request_has_an_server_address()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        await using var connection = new ClientConnection(new Uri("ice://localhost:10000"));

        var locationResolver = new NotCalledLocationResolver();
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var serviceAddress = new ServiceAddress(new Uri("ice://localhost:10000/path"));
        using var request = new OutgoingRequest(serviceAddress);

        await sut.InvokeAsync(request, default);

        Assert.That(locationResolver.Called, Is.False);
    }

    /// <summary>Verifies that the locator interceptor correctly resolves an adapter-id using the given location
    /// resolver.</summary>
    [Test]
    public async Task Resolve_adapter_id()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var expected = new ServiceAddress(new Uri("ice://localhost:10000/foo"));
        var locationResolver = new MockLocationResolver(expected, adapterId: true);
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var serviceAddress = new ServiceAddress(Protocol.Ice)
        {
            Params = new Dictionary<string, string> { ["adapter-id"] = "foo" }.ToImmutableDictionary()
        };
        using var request = new OutgoingRequest(serviceAddress);

        await sut.InvokeAsync(request, default);

        IServerAddressFeature? serverAddressFeature = request.Features.Get<IServerAddressFeature>();
        Assert.That(serverAddressFeature, Is.Not.Null);
        Assert.That(serverAddressFeature!.ServerAddress, Is.EqualTo(expected.ServerAddress));
    }

    /// <summary>Verifies that the locator interceptor correctly resolves a well-known proxy using the given
    /// location resolver.</summary>
    [Test]
    public async Task Resolve_well_known_proxy()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var expected = new ServiceAddress(new Uri("ice://localhost:10000/foo"));
        var locationResolver = new MockLocationResolver(expected, adapterId: false);
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var serviceAddress = new ServiceAddress(Protocol.Ice) { Path = "/foo" };
        using var request = new OutgoingRequest(serviceAddress);

        await sut.InvokeAsync(request, default);

        IServerAddressFeature? serverAddressFeature = request.Features.Get<IServerAddressFeature>();
        Assert.That(serverAddressFeature, Is.Not.Null);
        Assert.That(serverAddressFeature!.ServerAddress, Is.EqualTo(expected.ServerAddress));
    }

    /// <summary>Verifies that the locator interceptor set the refresh cache parameter on the second attempt to resolve
    /// a location if the first attempt returned a cached result.</summary>
    [Test]
    public async Task Resolve_refresh_cache_on_the_second_lookup()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var locationResolver = new MockCachedLocationResolver();
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var serviceAddress = new ServiceAddress(Protocol.Ice) { Path = "/foo" };
        using var request = new OutgoingRequest(serviceAddress);

        await sut.InvokeAsync(request, default);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(locationResolver.RefreshCache, Is.True);
    }

    /// <summary>Verifies that the locator interceptor does not set the refresh cache parameter on the second attempt
    /// to resolve a location if the first attempt returned a non cached result.</summary>
    [Test]
    public async Task Resolve_does_not_refresh_cache_after_getting_a_fresh_server_address()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var locationResolver = new MockNonCachedLocationResolver();
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var serviceAddress = new ServiceAddress(Protocol.Ice) { Path = "/foo" };
        using var request = new OutgoingRequest(serviceAddress);

        await sut.InvokeAsync(request, default);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(locationResolver.RefreshCache, Is.False);
    }

    // A mock location resolver that remember if it was called
    private sealed class NotCalledLocationResolver : ILocationResolver
    {
        public bool Called { get; set; }

        public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancellationToken)
        {
            Called = true;
            return new((null, false));
        }
    }

    // A mock location resolver that remember if it was called
    private sealed class MockLocationResolver : ILocationResolver
    {
        private readonly ServiceAddress _serviceAddress;
        private readonly bool _adapterId;

        public MockLocationResolver(ServiceAddress serviceAddress, bool adapterId)
        {
            _serviceAddress = serviceAddress;
            _adapterId = adapterId;
        }

        public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancellationToken) => new((_adapterId == location.IsAdapterId ? _serviceAddress : null, false));
    }

    // A mock location resolver that return cached and non cached server addresses depending on the refreshCache parameter
    private sealed class MockCachedLocationResolver : ILocationResolver
    {
        /// <summary><see langword="true" /> if the last call asked to refresh the cache otherwise,
        /// <see langword="false" />.</summary>
        public bool RefreshCache { get; set; }
        private readonly ServiceAddress _serviceAddress = new(new Uri("ice://localhost:10000/foo"));

        public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancellationToken)
        {
            RefreshCache = refreshCache;
            return new((_serviceAddress, !refreshCache));
        }
    }

    // A mock location resolver that always return non cached server addresses
    private sealed class MockNonCachedLocationResolver : ILocationResolver
    {
        /// <summary><see langword="true" /> if the last call asked to refresh the cache otherwise,
        /// <see langword="false" />.</summary>
        public bool RefreshCache { get; set; }
        private readonly ServiceAddress _serviceAddress = new(new Uri("ice://localhost:10000/foo"));

        public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancellationToken)
        {
            RefreshCache = refreshCache;
            return new((_serviceAddress, false));
        }
    }
}
