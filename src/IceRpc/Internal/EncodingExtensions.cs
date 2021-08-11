// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    internal static class EncodingExtensions
    {
        internal static void CheckSupportedIceEncoding(this Encoding encoding)
        {
            if (encoding != Encoding.Ice11 && encoding != Encoding.Ice20)
            {
                throw new NotSupportedException($"encoding '{encoding}' is not a supported by this IceRPC runtime");
            }
        }
    }
}
