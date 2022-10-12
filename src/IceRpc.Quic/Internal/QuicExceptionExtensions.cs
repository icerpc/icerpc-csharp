// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]

internal static class QuicExceptionExtensions
{
    /// <summary>Converts a <see cref="QuicException"/> into a <see cref="TransportException"/>.</summary>
    internal static Exception ToTransportException(this QuicException exception)
    {
        return exception.QuicError switch
        {
            QuicError.ConnectionAborted when exception.ApplicationErrorCode is not null =>
                ToTransportException(TransportErrorCode.ConnectionClosed, exception),
            QuicError.OperationAborted => ToTransportException(TransportErrorCode.ConnectionReset, exception),
            QuicError.ConnectionTimeout => ToTransportException(TransportErrorCode.ConnectionReset, exception),
            QuicError.ConnectionRefused => ToTransportException(TransportErrorCode.ConnectionRefused, exception),
            QuicError.AddressInUse => ToTransportException(TransportErrorCode.AddressInUse, exception),
            _ => ToTransportException(TransportErrorCode.Unspecified, exception)
        };

        static TransportException ToTransportException(TransportErrorCode errorCode, QuicException exception) =>
            exception.ApplicationErrorCode is null ?
                new(errorCode, exception) :
                new(errorCode, (ulong)exception.ApplicationErrorCode, exception);
    }
}
