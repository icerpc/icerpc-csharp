// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>Slic stream errors.</summary>
    internal enum SlicStreamError : int
    {
        /// <summary>The stream was successfully completed.</summary>
        NoError,
        /// <summary>The stream was completed with an unexpected error.</summary>
        UnexpectedError,
    }

    internal static class SlicStreamErrorExtensions
    {
        internal static ulong ToError(this SlicStreamError error) =>
            (ulong)MultiplexedStreamErrorKind.Transport | (ulong)error;
    }
}
