// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class QuicExceptionExtensions
{
    /// <summary>Converts a <see cref="QuicException"/> into an <see cref="IceRpcException"/>.</summary>
    internal static IceRpcException ToIceRpcException(this QuicException exception, string? message = null) =>
        exception.QuicError switch
        {
            QuicError.AddressInUse => new IceRpcException(IceRpcError.AddressInUse, message, exception),
            QuicError.ConnectionAborted =>
                exception.ApplicationErrorCode is long applicationErrorCode ?
                    applicationErrorCode switch
                    {
                        (long)MultiplexedConnectionCloseError.NoError =>
                            new IceRpcException(IceRpcError.ConnectionClosedByPeer, message),
                        (long)MultiplexedConnectionCloseError.ServerBusy =>
                            new IceRpcException(IceRpcError.ServerBusy, message),
                        _ => new IceRpcException(IceRpcError.ConnectionAborted, message, exception),
                    } :
                    new IceRpcException(IceRpcError.ConnectionAborted, message, exception),
            QuicError.ConnectionRefused => new IceRpcException(IceRpcError.ConnectionRefused, message, exception),
            QuicError.ConnectionTimeout => new IceRpcException(IceRpcError.ConnectionAborted, message, exception),
            QuicError.OperationAborted => new IceRpcException(IceRpcError.OperationAborted, message, exception),
            QuicError.StreamAborted => new IceRpcException(IceRpcError.TruncatedData, message, exception),

            _ => new IceRpcException(IceRpcError.IceRpcError, message, exception)
        };
}
