// Copyright (c) ZeroC, Inc.

using System;

namespace IceRpc.Ice.Generator.Tests;

internal class InvalidProxy : IIceProxy
{
    public IceEncodeOptions? EncodeOptions => throw new NotImplementedException();

    public IInvoker Invoker => throw new NotImplementedException();

    public ServiceAddress ServiceAddress => throw new NotImplementedException();

    internal static InvalidProxy Instance { get; } = new InvalidProxy();

    private InvalidProxy()
    {
    }
}
