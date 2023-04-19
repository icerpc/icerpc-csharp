// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice;

namespace IceRpc.Locator.Tests;

internal sealed class FakeLocator : ILocator
{
    internal int Resolved { get; set; }
    
    private readonly ServiceAddress _serviceAddress;
    private readonly bool _adapterId;

    public FakeLocator(ServiceAddress serviceAddress, bool adapterId)
    {
        _serviceAddress = serviceAddress;
        _adapterId = adapterId;
    }

    public Task<ServiceAddress?> FindAdapterByIdAsync(
        string id, IFeatureCollection? features,
        CancellationToken cancellationToken)
    {
        Resolved++;
        return Task.FromResult(id == "good" && _adapterId ? _serviceAddress : null);
    }

    public Task<ServiceAddress?> FindObjectByIdAsync(
        string id,
        IFeatureCollection? features,
        CancellationToken cancellationToken)
    {
        Resolved++;
        return Task.FromResult(id == "good" && !_adapterId ? _serviceAddress : null);
    }

    Task<LocatorRegistryProxy?> ILocator.GetRegistryAsync(
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
