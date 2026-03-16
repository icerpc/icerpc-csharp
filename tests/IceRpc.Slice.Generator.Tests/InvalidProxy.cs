// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generator.Tests;

internal class InvalidProxy : ISliceProxy
{
    internal static InvalidProxy Instance { get; } = new InvalidProxy();

    public SliceEncodeOptions? EncodeOptions
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

    private InvalidProxy() { }
}
