// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc
{
    /// <summary>The default proxy parser, that parses ice and ice+transport URIs.</summary>
    // TODO: switch to icerpc / icerpc+
    public class UriProxyParser : IProxyParser
    {
        /// <summary>The instance of the UriProxyParser.</summary>
        public static IProxyParser Instance { get; } = new UriProxyParser();

        /// <inheritdoc/>
        public Proxy Parse(string s, IInvoker? invoker = null)
        {
            Proxy proxy = IceUriParser.ParseProxyUri(s.Trim());
            proxy.Invoker = invoker;
            return proxy;
        }

        private UriProxyParser()
        {
            // ensures it's a singleton
        }
    }
}
