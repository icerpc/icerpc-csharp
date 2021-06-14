// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc.Internal
{
    // Definitions for the ice2 protocol.

    internal static class Ice2Definitions
    {
        internal static readonly Encoding Encoding = Encoding.V20;

        // TODO: some tests fail on macOS when the value is ReadOnlyMemory<byte>.Empty. Need to figure out why.
        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload11 = Array.Empty<byte>();

        // The only byte is for the compression format.
        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload20 = new byte[] { 0 };

        /// <summary>Returns the payload of an ice2 request frame for an operation with no argument.</summary>
        /// <param name="encoding">The encoding of this empty args payload.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetEmptyArgsPayload(Encoding encoding) =>
            GetVoidReturnValuePayload(encoding);

        /// <summary>Returns the payload of an ice2 response frame for an operation returning void.</summary>
        /// <param name="encoding">The encoding of this void return.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetVoidReturnValuePayload(Encoding encoding)
        {
            encoding.CheckSupported();
            return encoding == Encoding.V11 ? _voidReturnValuePayload11 : _voidReturnValuePayload20;
        }
    }
}
