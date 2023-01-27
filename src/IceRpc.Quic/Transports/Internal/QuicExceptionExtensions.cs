// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Quic;

namespace IceRpc.Transports.Internal;

internal static class QuicExceptionExtensions
{
    /// <summary>Converts a <see cref="QuicException"/> into an <see cref="IceRpcException"/>.</summary>
    internal static IceRpcException ToIceRpcException(this QuicException exception) =>
        exception.QuicError switch
        {
            QuicError.AddressInUse => new IceRpcException(IceRpcError.AddressInUse, exception),
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
                                $"The connection was closed by the peer with error '{MultiplexedConnectionCloseError.Aborted}'."),
                        _ => new IceRpcException(
                                IceRpcError.ConnectionAborted,
                                $"The connection was aborted by the peer with an unknown application error code: '{applicationErrorCode}'"),
                    } :
                    new IceRpcException(IceRpcError.ConnectionAborted, exception), // TODO: does this ever happen?
            QuicError.ConnectionRefused => new IceRpcException(IceRpcError.ConnectionRefused, exception),
            QuicError.ConnectionTimeout => new IceRpcException(IceRpcError.ConnectionAborted, exception),
            QuicError.HostUnreachable => new IceRpcException(IceRpcError.ServerUnreachable, exception),
            QuicError.OperationAborted => new IceRpcException(IceRpcError.OperationAborted, exception),
            QuicError.StreamAborted => new IceRpcException(IceRpcError.TruncatedData, exception),

            _ => new IceRpcException(IceRpcError.IceRpcError, exception)
        };
}
