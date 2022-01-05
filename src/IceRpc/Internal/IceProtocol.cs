// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>The Ice protocol class.</summary>
    internal sealed class IceProtocol : Protocol
    {
        /// <summary>The Ice protocol singleton.</summary>
        internal static IceProtocol Instance { get; } = new();
        internal override IceEncoding? IceEncoding => Encoding.Slice11;

        internal override bool HasFieldSupport => false;

        internal IProtocolConnectionFactory<ISimpleNetworkConnection> ProtocolConnectionFactory { get; } =
            new IceProtocolConnectionFactory();

        private IceProtocol()
            : base(ProtocolCode.Ice, IceName)
        {
        }
    }
}
