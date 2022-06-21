// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>The IceRPC protocol class.</summary>
internal sealed class IceRpcProtocol : Protocol
{
    public override int DefaultUriPort => 4062;

    public override bool HasFields => true;

    public override bool IsSupported => true;

    public override IMultiplexedStreamErrorCodeConverter? MultiplexedStreamErrorCodeConverter { get; } =
        new ErrorCodeConverter();

    /// <summary>Gets the IceRpc protocol singleton.</summary>
    internal static IceRpcProtocol Instance { get; } = new();

    internal override SliceEncoding SliceEncoding => SliceEncoding.Slice2;

    private IceRpcProtocol()
        : base(IceRpcName)
    {
    }

    private class ErrorCodeConverter : IMultiplexedStreamErrorCodeConverter
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
}
