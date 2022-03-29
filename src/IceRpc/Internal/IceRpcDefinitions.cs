// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Internal
{
    // Definitions for the icerpc protocol.

    internal static class IceRpcDefinitions
    {
        internal static readonly Encoding Encoding = SliceEncoding.Slice20;
    }
}
