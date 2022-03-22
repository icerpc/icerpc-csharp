// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Interop.Tests;

public class LocatorInterceptorTests
{
    /// <summary>Verifies that the location resolver is not called when the request carries a connection.</summary>
    [Test]
    public async Task Location_resolver_not_called_if_the_request_has_a_connection()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        await using var connection = new Connection(new Configure.ConnectionOptions()
        {
            RemoteEndpoint = "ice://localhost:10000"
        });
        var locationResolver = new NotCalledLocationResolver();
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var request = new OutgoingRequest(new Proxy(Protocol.Ice) { Path = "/path" })
        {
            Connection = connection
        };

        await sut.InvokeAsync(request, default);

        Assert.That(locationResolver.Called, Is.False);
    }

    /// <summary>Verifies that the locator interceptor can correctly resolver an adapter-id using the given location
    /// resolver.</summary>
    [Test]
    public async Task Resolve_adapter_id()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var expected = Proxy.Parse("ice://localhost:10000/foo");
        var locationResolver = new MockLocationResolver(expected, adapterId: true);
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var proxy = new Proxy(Protocol.Ice)
        {
            Params = new Dictionary<string, string> { ["adapter-id"] = "foo" }.ToImmutableDictionary()
        };
        var request = new OutgoingRequest(proxy);

        await sut.InvokeAsync(request, default);

        EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
        Assert.That(endpointSelection, Is.Not.Null);
        Assert.That(endpointSelection.Endpoint, Is.EqualTo(expected.Endpoint));
    }

    /// <summary>Verifies that the locator interceptor can correctly resolver a well-known proxy using the given
    /// location resolver.</summary>
    [Test]
    public async Task Resolve_well_known_proxy()
    {
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var expected = Proxy.Parse("ice://localhost:10000/foo");
        var locationResolver = new MockLocationResolver(expected, adapterId: false);
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var proxy = new Proxy(Protocol.Ice) { Path = "/foo" };
        var request = new OutgoingRequest(proxy);

        await sut.InvokeAsync(request, default);

        EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
        Assert.That(endpointSelection, Is.Not.Null);
        Assert.That(endpointSelection.Endpoint, Is.EqualTo(expected.Endpoint));
    }

    /// <summary>Verifies that the locator interceptor set the refresh cache parameter on the second attempt to resolve
    /// a location if the first attempt returned a cached result.</summary>
    [Test]
    public async Task Resolve_refresh_cache_on_the_second_lookup()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var cached = Proxy.Parse("ice://cached.loc:10000/foo");
        var fresh = Proxy.Parse("ice://fresh.loc:10000/foo");
        var locationResolver = new MockCachedLocationResolver(cached, fresh);
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var proxy = new Proxy(Protocol.Ice) { Path = "/foo" };
        var request = new OutgoingRequest(proxy);

        await sut.InvokeAsync(request, default);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
        Assert.That(endpointSelection, Is.Not.Null);
        Assert.That(endpointSelection.Endpoint, Is.EqualTo(fresh.Endpoint));
    }

    /// <summary>Verifies that the locator interceptor does not set the refresh cache parameter on the second attempt
    /// to resolve a location if the first attempt returned a non cached result.</summary>
    [Test]
    public async Task Resolve_does_not_refresh_cache_after_getting_a_fresh_endpoint()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request)));
        var cached = Proxy.Parse("ice://cached.loc:10000/foo");
        var fresh = Proxy.Parse("ice://fresh.loc:10000/foo");
        var locationResolver = new MockCachedLocationResolver2(cached, fresh);
        var sut = new LocatorInterceptor(invoker, locationResolver);
        var proxy = new Proxy(Protocol.Ice) { Path = "/foo" };
        var request = new OutgoingRequest(proxy);

        await sut.InvokeAsync(request, default);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
        Assert.That(endpointSelection, Is.Not.Null);
        Assert.That(endpointSelection.Endpoint, Is.EqualTo(cached.Endpoint));
    }

    // A mock location resolver that remember if it was called
    private class NotCalledLocationResolver : ILocationResolver
    {
        public bool Called { get; set; }

        public ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel)
        {
            Called = true;
            return new((null, false));
        }
    }

    // A mock location resolver that remember if it was called
    private class MockLocationResolver : ILocationResolver
    {
        private readonly Proxy _proxy;
        private readonly bool _adapterId;

        public MockLocationResolver(Proxy proxy, bool adapterId)
        {
            _proxy = proxy;
            _adapterId = adapterId;
        }

        public ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel) => new((_adapterId == location.IsAdapterId ? _proxy : null, false));
    }

    // A mock location resolver that return cached and non cached proxy depending on the refreshCache parameter
    private class MockCachedLocationResolver : ILocationResolver
    {
        private readonly Proxy _cached;
        private readonly Proxy _fresh;

        public MockCachedLocationResolver(Proxy cached, Proxy fresh)
        {
            _cached = cached;
            _fresh = fresh;
        }

        public ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel) => new((refreshCache ? _fresh : _cached, !refreshCache));
    }

    // A mock location resolver that return cached and non cached proxy depending on the refreshCache parameter
    private class MockCachedLocationResolver2 : ILocationResolver
    {
        private readonly Proxy _cached;
        private readonly Proxy _fresh;

        public MockCachedLocationResolver2(Proxy cached, Proxy fresh)
        {
            _cached = cached;
            _fresh = fresh;
        }

        public ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel) => new((refreshCache ? _fresh : _cached, false));
    }
}
