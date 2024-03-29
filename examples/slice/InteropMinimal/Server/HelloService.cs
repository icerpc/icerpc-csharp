// Copyright (c) ZeroC, Inc.

using Demo;
using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Slice.Ice;

namespace MinimalServer;

/// <summary>An HelloService is an IceRPC service that implements Slice interface 'Hello'.</summary>
// We implement IIceObjectService as well but without actually implementing any method. Without this IIceObjectService,
// the "checked cast" from the Ice client would fail.
[SliceService]
internal partial class HelloService : IHelloService, IIceObjectService
{
    public ValueTask SayHelloAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine("Hello, World!");
        return default;
    }
}
