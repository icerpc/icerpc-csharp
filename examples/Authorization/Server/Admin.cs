// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

public class AdminService : Service, IAdmin
{
    private readonly HelloService _helloService;

    public AdminService(HelloService helloService) => _helloService = helloService;

    public ValueTask ChangeGreetingAsync(string greeting, IFeatureCollection features, CancellationToken cancellationToken)
    {
        _helloService.Greeting = greeting;
        return default;
    }
}
