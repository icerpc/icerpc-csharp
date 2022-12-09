// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>
/// The implementation of the IAdmin service. This service is used to change the greeting and requires callers to be
/// authenticated.
/// </summary>
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
