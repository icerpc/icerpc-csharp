// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Generator.Tests;

internal class InvalidProxy : IIceProxy
{
    public IceEncodeOptions? EncodeOptions
    {
        get => throw new NotImplementedException();
        init { }
    }

    public IInvoker Invoker
    {
        get => throw new NotImplementedException();
        init { }
    }

    public ServiceAddress ServiceAddress
    {
        get => throw new NotImplementedException();
        init { }
    }

    internal static InvalidProxy Instance { get; } = new InvalidProxy();

    private InvalidProxy()
    {
    }
}
