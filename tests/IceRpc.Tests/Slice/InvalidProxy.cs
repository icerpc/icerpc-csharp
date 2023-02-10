// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;

namespace IceRpc.Tests.Slice;

internal static class InvalidProxy
{
    internal static GenericProxy Instance { get; } = new()
    {
        Invoker = NotImplementedInvoker.Instance,
        ServiceAddress = null!
    };
}
