// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;

namespace IceRpc.Slice.Tests;

internal static class InvalidProxy
{
    internal static GenericProxy Instance { get; } = new()
    {
        Invoker = NotImplementedInvoker.Instance,
        ServiceAddress = null!
    };
}
