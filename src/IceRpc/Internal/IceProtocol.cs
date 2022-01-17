// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;

namespace IceRpc.Internal
{
    /// <summary>The Ice protocol class.</summary>
    internal sealed class IceProtocol : Protocol
    {
        public override int DefaultUriPort => 4061;

        public override bool HasFragment => true;
        public override bool IsSupported => true;

        /// <summary>The Ice protocol singleton.</summary>
        internal static IceProtocol Instance { get; } = new();

        internal override IceEncoding? SliceEncoding => Encoding.Slice11;

        internal IProtocolConnectionFactory<ISimpleNetworkConnection> ProtocolConnectionFactory { get; } =
            new IceProtocolConnectionFactory();

        private IceProtocol()
            : base(IceName)
        {
        }
    }
}
