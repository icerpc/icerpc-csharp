// Copyright (c) ZeroC, Inc.

using System.Net.Quic;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Quic.Internal;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
internal static class QuicExceptionExtensions
{
    /// <summary>Extension methods for <see cref="QuicException" />.</summary>
    /// <param name="exception">The exception to convert.</param>
    extension(QuicException exception)
    {
        /// <summary>Converts a <see cref="QuicException"/> into an <see cref="IceRpcException"/>.</summary>
        internal IceRpcException ToIceRpcException() =>
        exception.QuicError switch
        {
            QuicError.ConnectionAborted =>
                exception.ApplicationErrorCode is long applicationErrorCode ?
                    applicationErrorCode switch
                    {
                        (long)MultiplexedConnectionCloseError.NoError =>
                            new IceRpcException(IceRpcError.ConnectionClosedByPeer),
                        (long)MultiplexedConnectionCloseError.Refused =>
                            new IceRpcException(IceRpcError.ConnectionRefused),
                        (long)MultiplexedConnectionCloseError.ServerBusy =>
                            new IceRpcException(IceRpcError.ServerBusy),
                        (long)MultiplexedConnectionCloseError.Aborted =>
                            new IceRpcException(
                                IceRpcError.ConnectionAborted,
                                "The connection was aborted by the peer."),
                        _ => new IceRpcException(
                                IceRpcError.ConnectionAborted,
                                "The connection was aborted by the peer with an unknown application error code: " +
                                $"'{applicationErrorCode}'"),
                    } :
                    // An application error code should always be set with QuicError.ConnectionAborted.
                    new IceRpcException(IceRpcError.IceRpcError, exception),
            QuicError.ConnectionRefused => new IceRpcException(IceRpcError.ConnectionRefused, exception),
            QuicError.ConnectionTimeout => new IceRpcException(IceRpcError.ConnectionAborted, exception),
            QuicError.ConnectionIdle => new IceRpcException(IceRpcError.ConnectionIdle, exception),
            QuicError.OperationAborted => new IceRpcException(IceRpcError.OperationAborted, exception),
            QuicError.AlpnInUse => new IceRpcException(IceRpcError.AddressInUse, exception),
            QuicError.StreamAborted =>
                exception.ApplicationErrorCode is long applicationErrorCode ?
                    applicationErrorCode == 0 ?
                        new IceRpcException(IceRpcError.TruncatedData, exception) :
                        new IceRpcException(
                            IceRpcError.TruncatedData,
                            "The stream was aborted by the peer with an unknown application error code: " +
                            $"'{applicationErrorCode}'") :
                    // An application error code should always be set with QuicError.StreamAborted.
                    new IceRpcException(IceRpcError.IceRpcError, exception),
            _ => new IceRpcException(IceRpcError.IceRpcError, exception)
        };
    }
}
