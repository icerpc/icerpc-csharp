// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>The IceRPC protocol class.</summary>
internal sealed class IceRpcProtocol : Protocol
{
    /// <summary>Gets the IceRpc protocol singleton.</summary>
    internal static IceRpcProtocol Instance { get; } = new();

    internal IMultiplexedStreamErrorCodeConverter MultiplexedStreamErrorCodeConverter { get; }
        = new ErrorCodeConverter();

    private IceRpcProtocol()
        : base(
            name: "icerpc",
            defaultPort: 4062,
            hasFields: true,
            hasFragment: false,
            byteValue: 2,
            sliceEncoding: SliceEncoding.Slice2)
    {
    }

    private class ErrorCodeConverter : IMultiplexedStreamErrorCodeConverter
    {
        public Exception? FromErrorCode(ulong errorCode) =>
            (IceRpcStreamErrorCode)errorCode switch
            {
                IceRpcStreamErrorCode.NoError => null,

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

                OperationCanceledException => (ulong)IceRpcStreamErrorCode.Canceled,

                ConnectionClosedException => (ulong)IceRpcStreamErrorCode.ConnectionShutdown,

                IceRpcProtocolStreamException streamException => (ulong)streamException.ErrorCode,

                InvalidDataException => (ulong)IceRpcStreamErrorCode.InvalidData,

                _ => (ulong)IceRpcStreamErrorCode.Unspecified
            };
    }
}
