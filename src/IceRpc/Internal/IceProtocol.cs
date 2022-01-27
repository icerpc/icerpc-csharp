// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using System.Collections.Immutable;

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

        internal override SliceEncoding? SliceEncoding => Encoding.Slice11;

        internal IProtocolConnectionFactory<ISimpleNetworkConnection> ProtocolConnectionFactory { get; } =
            new IceProtocolConnectionFactory();

        /// <summary>Checks if this absolute path holds a valid identity.</summary>
        internal override void CheckPath(string uriPath)
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

        /// <summary>Checks if the proxy parameters are valid. The only valid parameter is adapter-id with a non-empty
        /// value.</summary>
        internal override void CheckProxyParams(ImmutableDictionary<string, string> proxyParams)
        {
            foreach ((string name, string value) in proxyParams)
            {
                if (name == "adapter-id")
                {
                    if (value.Length == 0)
                    {
                        throw new FormatException("the value of the adapter-id parameter cannot be empty");
                    }
                }
                else
                {
                    throw new FormatException($"'{name}' is not a valid ice proxy parameter");
                }
            }
        }

        private IceProtocol()
            : base(IceName)
        {
        }
    }
}
