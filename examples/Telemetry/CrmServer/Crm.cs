// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace TelemetryExample;

internal class Crm : Service, ICrmService
{
    private readonly List<string> _customers = new();

    public ValueTask<bool> TryAddCustomerAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        if (_customers.Contains(name))
        {
            return new(false);
        }
        _customers.Add(name);
        return new(true);
    }
}
