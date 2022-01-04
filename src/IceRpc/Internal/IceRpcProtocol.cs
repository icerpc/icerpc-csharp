// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>The IceRPC protocol class.</summary>
    internal sealed class IceRpcProtocol : Protocol
    {
        /// <summary>The IceRpc protocol singleton.</summary>
        internal static IceRpcProtocol Instance { get; } = new();

        internal override IceEncoding? IceEncoding => Encoding.Slice20;

        internal override bool HasFieldSupport => true;

        internal IProtocolConnectionFactory<IMultiplexedNetworkConnection> ProtocolConnectionFactory { get; } =
            new IceRpcProtocolConnectionFactory();

        private IceRpcProtocol()
            : base(ProtocolCode.IceRpc, IceRpcName)
        {
        }
    }
}
