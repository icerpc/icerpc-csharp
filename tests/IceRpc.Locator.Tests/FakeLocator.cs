// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice;

namespace IceRpc.Locator.Tests;

/// <summary>An <see cref="ILocator"/> implementation used by the tests.</summary>
internal sealed class FakeLocator : ILocator
{
    internal int ResolvedCount { get; set; }
    
    private readonly ServiceAddress _serviceAddress;
    private readonly bool _adapterId;

    public Task<ServiceAddress?> FindAdapterByIdAsync(
        string id,
        IFeatureCollection? features,
        CancellationToken cancellationToken)
    {
        ResolvedCount++;
        return Task.FromResult(id == "good" && _adapterId ? _serviceAddress : null);
    }

    public Task<ServiceAddress?> FindObjectByIdAsync(
        string id,
        IFeatureCollection? features,
        CancellationToken cancellationToken)
    {
        ResolvedCount++;
        return Task.FromResult(id == "good" && !_adapterId ? _serviceAddress : null);
    }

    Task<LocatorRegistryProxy?> ILocator.GetRegistryAsync(
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    /// <summary>Constructs a locator that resolves the object or adapter-id with value "good" to the given service
    /// address.</summary>
    /// <param name="serviceAddress">The <see cref="ServiceAddress"/> returned when the locator resolves an "id" with
    /// the value "good".</param>
    /// <param name="adapterId">Whether the <paramref name="serviceAddress"/> should be returned when resolving an
    /// object by ID or an adapter ID.</param>
    internal FakeLocator(ServiceAddress serviceAddress, bool adapterId)
    {
        _serviceAddress = serviceAddress;
        _adapterId = adapterId;
    }
}
