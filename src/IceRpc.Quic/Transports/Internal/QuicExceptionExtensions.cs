// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class QuicExceptionExtensions
{
    /// <summary>Converts a <see cref="QuicException"/> into a <see cref="TransportException"/>.</summary>
    internal static TransportException ToTransportException(this QuicException exception) =>
        exception.QuicError switch
        {
            QuicError.AddressInUse => new TransportException(TransportErrorCode.AddressInUse, exception),
            QuicError.ConnectionAborted =>
                exception.ApplicationErrorCode is null ?
                    new TransportException(TransportErrorCode.ConnectionAborted, exception) :
                    new TransportException(
                        TransportErrorCode.ConnectionAborted,
                        (ulong)exception.ApplicationErrorCode,
                        exception),
            QuicError.ConnectionRefused => new TransportException(TransportErrorCode.ConnectionRefused, exception),
            QuicError.ConnectionTimeout => new TransportException(TransportErrorCode.ConnectionAborted, exception),
            QuicError.InternalError => new TransportException(TransportErrorCode.InternalError, exception),
            QuicError.OperationAborted => new TransportException(TransportErrorCode.OperationAborted, exception),

            _ => new TransportException(TransportErrorCode.Unspecified, exception)
        };
}
