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
        // We don't map error codes received from the peer to exceptions such as OperationCanceledException,
        // ConnectionException or InvalidData as it would be confusing: OperationCanceledException etc. are local
        // exceptions that don't report remote events.
        public Exception? FromErrorCode(ulong errorCode) =>
            (IceRpcStreamErrorCode)errorCode switch
            {
                IceRpcStreamErrorCode.NoError => null,
                _ => new IceRpcProtocolStreamException((IceRpcStreamErrorCode)errorCode)
            };

        public ulong ToErrorCode(Exception? exception) =>
            exception switch
            {
                null => (ulong)IceRpcStreamErrorCode.NoError,

                OperationCanceledException => (ulong)IceRpcStreamErrorCode.Canceled,

                ConnectionException connectionException =>
                    connectionException.ErrorCode.IsClosedErrorCode() ?
                        (ulong)IceRpcStreamErrorCode.ConnectionShutdown :
                        (ulong)IceRpcStreamErrorCode.Unspecified,

                IceRpcProtocolStreamException streamException => (ulong)streamException.ErrorCode,

                InvalidDataException => (ulong)IceRpcStreamErrorCode.InvalidData,

                _ => (ulong)IceRpcStreamErrorCode.Unspecified
            };
    }
}
