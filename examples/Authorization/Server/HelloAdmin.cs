// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A service that implements Slice interface HelloAdmin. It is used to change the greeting and requires
/// callers to be authenticated.</summary>
internal class HelloAdmin : Service, IHelloAdminService
{
    private readonly Hello _hello;

    internal HelloAdmin(Hello hello) => _hello = hello;

    public ValueTask ChangeGreetingAsync(
        string greeting,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        _hello.Greeting = greeting;
        return default;
    }
}
