// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Crm : Service, ICrm
{
    private readonly List<string> _customers = new();

    public ValueTask<bool> TryAddCustomerAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        if (_customers.Contains(name))
        {
            return new(false);
        }
        _customers.Add(name);
        return new(true);
    }
}
