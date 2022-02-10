// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Internal
{
    /// <summary>A dispatcher that always throws <see cref="ServiceNotFoundException"/> and can be used
    /// as a local default when there isn't a dispatcher.</summary>
    internal static class NullDispatcher
    {
        /// <summary>The dispatcher instance.</summary>
        internal static IDispatcher Instance { get; } =
            new InlineDispatcher((request, cancel) => throw new ServiceNotFoundException(RetryPolicy.OtherReplica));
    }
}
