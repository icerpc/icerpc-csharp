// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc;

public class LocatorEndpointFinderTests
{
    [Test]
    public async Task Find_adapter_by_id()
    {
        var expectedProxy = ServicePrx.Parse("ice://localhost/dummy:10000");
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new FakeLocatorPrx(expectedProxy));
        var location = new Location() { IsAdapterId = true, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.EqualTo(expectedProxy.Proxy));
    }

    [Test]
    public async Task Find_adapter_by_id_not_found()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new NotFoundLocatorPrx());
        var location = new Location() { IsAdapterId = true, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.Null);
    }

    [Test]
    public async Task Find_object_by_id()
    {
        var expectedProxy = ServicePrx.Parse("ice://localhost/dummy:10000");
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new FakeLocatorPrx(expectedProxy));
        var location = new Location() { IsAdapterId = false, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.EqualTo(expectedProxy.Proxy));
    }

    [Test]
    public async Task Find_object_by_id_not_found()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new NotFoundLocatorPrx());
        var location = new Location() { IsAdapterId = false, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.Null);
    }

    private class FakeLocatorPrx : ILocatorPrx
    {
        private readonly ServicePrx _service;

        public FakeLocatorPrx(ServicePrx service) => _service = service;

        public Task<ServicePrx?> FindAdapterByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            Task.FromResult<ServicePrx?>(id == "good" ? _service : null);

        public Task<ServicePrx?> FindObjectByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            Task.FromResult<ServicePrx?>(id == "good" ? _service : null);

        Task<LocatorRegistryPrx?> ILocatorPrx.GetRegistryAsync(Invocation? invocation, CancellationToken cancel) =>
            throw new NotImplementedException();
    }

    private class NotFoundLocatorPrx : ILocatorPrx
    {
        public Task<ServicePrx?> FindAdapterByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            throw new AdapterNotFoundException();

        public Task<ServicePrx?> FindObjectByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            throw new ObjectNotFoundException();

        public Task<LocatorRegistryPrx?> GetRegistryAsync(Invocation? invocation, CancellationToken cancel) =>
            throw new NotImplementedException();
    }
}