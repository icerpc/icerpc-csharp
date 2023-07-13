// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice.Ice;

namespace IceRpc.Locator.Tests;

internal sealed class NotFoundLocator : ILocator
{
    public Task<ServiceAddress?> FindAdapterByIdAsync(
        string id,
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new AdapterNotFoundException();

    public Task<ServiceAddress?> FindObjectByIdAsync(
        string id,
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new ObjectNotFoundException();

    public Task<LocatorRegistryProxy?> GetRegistryAsync(
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
