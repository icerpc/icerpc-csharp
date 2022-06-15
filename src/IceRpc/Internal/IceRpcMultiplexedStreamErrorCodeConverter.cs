// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="IMultiplexedStreamErrorCodeConverter"/> for the icerpc protocol.</summary>
internal class IceRpcMultiplexedStreamErrorCodeConverter : IMultiplexedStreamErrorCodeConverter
{
    public Exception? FromErrorCode(ulong errorCode) =>
        (IceRpcStreamErrorCode)errorCode switch
        {
            IceRpcStreamErrorCode.NoError => null,

            IceRpcStreamErrorCode.OperationCanceled =>
                new OperationCanceledException("the operation was canceled by the remote peer"),

            IceRpcStreamErrorCode.ConnectionShutdown =>
                new ConnectionClosedException("the connection was shut down by the remote peer"),

            IceRpcStreamErrorCode.InvalidData =>
                new InvalidDataException("the remote peer failed to decode data from the stream"),

            _ => new IceRpcProtocolStreamException((IceRpcStreamErrorCode)errorCode)
        };

    public ulong ToErrorCode(Exception? exception) =>
        exception switch
        {
            null => (ulong)IceRpcStreamErrorCode.NoError,

            ConnectionClosedException => (ulong)IceRpcStreamErrorCode.ConnectionShutdown,

            IceRpcProtocolStreamException streamException => (ulong)streamException.ErrorCode,

            OperationCanceledException => (ulong)IceRpcStreamErrorCode.OperationCanceled,

            InvalidDataException => (ulong)IceRpcStreamErrorCode.InvalidData,

            _ => (ulong)IceRpcStreamErrorCode.Unspecified
        };
}
