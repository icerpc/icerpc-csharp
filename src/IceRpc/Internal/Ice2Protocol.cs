// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>The Ice2 protocol class.</summary>
    internal sealed class Ice2Protocol : Protocol
    {
        /// <summary>The Ice2 protocol singleton.</summary>
        internal static Ice2Protocol Instance { get; } = new();

        internal override IceEncoding? IceEncoding => Encoding.Ice20;

        internal override bool HasFieldSupport => true;

        internal IProtocolConnectionFactory<IMultiplexedNetworkConnection> ProtocolConnectionFactory { get; } =
            new Ice2ProtocolConnectionFactory();

        private Ice2Protocol()
            : base(ProtocolCode.Ice2, Ice2Name)
        {
        }
    }
}
