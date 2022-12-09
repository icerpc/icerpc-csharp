// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>
/// The implementation of the IHelloAdmin service. This service is used to change the greeting and requires callers to be
/// authenticated.
/// </summary>
public class HelloAdminService : Service, IHelloAdmin
{
    private readonly HelloService _helloService;

    public HelloAdminService(HelloService helloService) => _helloService = helloService;

    public ValueTask ChangeGreetingAsync(
        string greeting,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        _helloService.Greeting = greeting;
        return default;
    }
}
