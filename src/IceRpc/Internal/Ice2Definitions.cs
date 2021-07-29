// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc.Internal
{
    // Definitions for the ice2 protocol.

    internal static class Ice2Definitions
    {
        internal static readonly Encoding Encoding = Encoding.V20;

        internal static readonly IceEncoding IceEncoding = IceEncoding.V20;

        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload11 = ReadOnlyMemory<byte>.Empty;

        // The only byte is for the compression format.
        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload20 = new byte[] { 0 };

        /// <summary>Returns the payload of an ice2 request frame for an operation with no argument.</summary>
        /// <param name="iceEncoding">The Ice encoding of this empty args payload.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetEmptyArgsPayload(IceEncoding iceEncoding) =>
            GetVoidReturnValuePayload(iceEncoding);

        /// <summary>Returns the payload of an ice2 response frame for an operation returning void.</summary>
        /// <param name="iceEncoding">The encoding of this void return.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetVoidReturnValuePayload(IceEncoding iceEncoding) =>
            iceEncoding == IceEncoding.V11 ? _voidReturnValuePayload11 : _voidReturnValuePayload20;
    }
}
