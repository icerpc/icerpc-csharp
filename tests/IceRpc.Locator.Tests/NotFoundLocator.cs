// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice;

namespace IceRpc.Locator.Tests;

internal sealed class NotFoundLocator : ILocator
{
    public Task<IceObjectProxy?> FindAdapterByIdAsync(
        string id,
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new AdapterNotFoundException();

    public Task<IceObjectProxy?> FindObjectByIdAsync(
        Identity id,
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new ObjectNotFoundException();

    public Task<LocatorRegistryProxy?> GetRegistryAsync(
        IFeatureCollection? features,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
