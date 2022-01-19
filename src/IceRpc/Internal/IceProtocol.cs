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

        /// <summary>Checks if this absolute path holds a valid identity.</summary>
        internal override void CheckUriPath(string uriPath)
        {
            string workingPath = uriPath[1..]; // removes leading /.
            int firstSlash = workingPath.IndexOf('/', StringComparison.Ordinal);

            string escapedName;

            if (firstSlash == -1)
            {
                escapedName = workingPath;
            }
            else
            {
                if (firstSlash != workingPath.LastIndexOf('/'))
                {
                    throw new FormatException($"too many slashes in path '{uriPath}'");
                }
                escapedName = workingPath[(firstSlash + 1)..];
            }

            if (escapedName.Length == 0)
            {
                throw new FormatException($"invalid empty identity name in path '{uriPath}'");
            }
        }

        private IceProtocol()
            : base(IceName)
        {
        }
    }
}
