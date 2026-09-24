// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Tests;

internal class InvalidProxy : ISliceProxy
{
    public SliceEncodeOptions? EncodeOptions => throw new NotImplementedException();

    public IInvoker Invoker => throw new NotImplementedException();

    public bool IsRelative => throw new NotImplementedException();

    public ServiceAddress ServiceAddress => throw new NotImplementedException();

    internal static InvalidProxy Instance { get; } = new InvalidProxy();

    private InvalidProxy()
    {
    }
}
