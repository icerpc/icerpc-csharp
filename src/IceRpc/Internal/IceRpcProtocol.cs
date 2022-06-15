// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>The IceRPC protocol class.</summary>
    internal sealed class IceRpcProtocol : Protocol
    {
        public override int DefaultUriPort => 4062;

        public override bool HasFields => true;

        public override bool IsSupported => true;

        public override Exception? FromStreamErrorCode(ulong errorCode) =>
            (IceRpcStreamErrorCode)errorCode switch
            {
                IceRpcStreamErrorCode.NoError => null,
                IceRpcStreamErrorCode.RequestCanceled =>
                    new OperationCanceledException("request canceled by peer"),
                IceRpcStreamErrorCode.ConnectionShutdown =>
                    new ConnectionClosedException("connection shutdown by peer"),
                _ => new IceRpcProtocolStreamException((IceRpcStreamErrorCode)errorCode)
            };

        public override ulong ToStreamErrorCode(Exception? exception)
        {
            if (exception == null)
            {
                return (ulong)IceRpcStreamErrorCode.NoError;
            }
            else
            {
                return exception switch
                {
                    ConnectionClosedException => (ulong)IceRpcStreamErrorCode.ConnectionShutdown,
                    IceRpcProtocolStreamException multiplexedStreamException =>
                        (ulong)multiplexedStreamException.ErrorCode,
                    OperationCanceledException => (ulong)IceRpcStreamErrorCode.RequestCanceled,
                    _ => (ulong)IceRpcStreamErrorCode.Unspecified
                };
            }
        }

        /// <summary>Gets the IceRpc protocol singleton.</summary>
        internal static IceRpcProtocol Instance { get; } = new();

        internal IProtocolConnectionFactory<IMultiplexedNetworkConnection> ProtocolConnectionFactory { get; } =
            new IceRpcProtocolConnectionFactory();

        internal override SliceEncoding SliceEncoding => SliceEncoding.Slice2;

        private IceRpcProtocol()
            : base(IceRpcName)
        {
        }
    }
}
