// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Internal
{
    /// <summary>A dispatcher that always throws a <see cref="DispatchException"/> and can be used as a local default
    /// when there is no dispatcher.</summary>
    internal static class NullDispatcher
    {
        /// <summary>The dispatcher instance.</summary>
        internal static IDispatcher Instance { get; } = new InlineDispatcher(
            (request, cancel) =>
                throw new DispatchException(DispatchErrorCode.ServiceNotFound, RetryPolicy.OtherReplica));
    }
}
