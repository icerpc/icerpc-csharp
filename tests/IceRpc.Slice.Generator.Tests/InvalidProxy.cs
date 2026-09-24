// Copyright (c) ZeroC, Inc.

using System;

namespace IceRpc.Slice.Generator.Tests;

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
