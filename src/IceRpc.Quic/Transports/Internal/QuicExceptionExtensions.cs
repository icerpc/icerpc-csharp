// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class QuicExceptionExtensions
{
    /// <summary>Converts a <see cref="QuicException"/> into a <see cref="IceRpcException"/>.</summary>
    internal static IceRpcException ToTransportException(this QuicException exception) =>
        exception.QuicError switch
        {
            QuicError.AddressInUse => new IceRpcException(IceRpcError.AddressInUse, exception),
            QuicError.ConnectionAborted =>
                exception.ApplicationErrorCode is null ?
                    new IceRpcException(IceRpcError.ConnectionAborted, exception) :
                    new IceRpcException(
                        IceRpcError.ConnectionAborted,
                        (ulong)exception.ApplicationErrorCode,
                        exception),
            QuicError.ConnectionRefused => new IceRpcException(IceRpcError.ConnectionRefused, exception),
            QuicError.ConnectionTimeout => new IceRpcException(IceRpcError.ConnectionAborted, exception),
            QuicError.InternalError => new IceRpcException(IceRpcError.InternalError, exception),
            QuicError.OperationAborted => new IceRpcException(IceRpcError.OperationAborted, exception),

            _ => new IceRpcException(IceRpcError.Unspecified, exception)
        };
}
