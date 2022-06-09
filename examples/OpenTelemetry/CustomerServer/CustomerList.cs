// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class CustomerList : Service, ICustomerList
{
    private readonly List<string> _customerList = new();

    public ValueTask<bool> TryAddCustomerAsync(string name, IFeatureCollection features, CancellationToken cancel)
    {
        if (_customerList.Contains(name))
        {
            return new(false);
        }
        _customerList.Add(name);
        return new(true);
    }
}
