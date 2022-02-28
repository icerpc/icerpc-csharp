// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>Slic stream errors.</summary>
    internal enum SlicStreamError : int
    {
        /// <summary>The stream was successfully completed.</summary>
        NoError,
        /// <summary>The stream was aborted.</summary>
        Aborted,
        /// <summary>The stream was completed with an unexpected error.</summary>
        UnexpectedError,
    }

    internal static class SlicStreamErrorExtensions
    {
        internal static long ToError(this SlicStreamError error) =>
            (long)MultiplexedStreamErrorKind.Transport | (long)error;

        internal static SlicStreamError? ToSlicError(this long error) =>
            (error >> 32) == (long)MultiplexedStreamErrorKind.Transport ? (SlicStreamError)error : null;

        internal static SlicStreamError? ToSlicError(this MultiplexedStreamAbortedException exception) =>
            exception.ErrorKind == MultiplexedStreamErrorKind.Transport ? (SlicStreamError)exception.ErrorCode : null;
    }
}
