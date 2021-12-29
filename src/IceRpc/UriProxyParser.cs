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
            // TODO: refactor
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("an empty string does not represent a proxy");
            }

            Proxy proxy = IceUriParser.IsProxyUri(proxyString) ?
                IceUriParser.ParseProxyUri(proxyString) : Ice1Parser.ParseProxyString(proxyString);

            proxy.Invoker = invoker;
            return proxy;
        }

        private UriProxyParser()
        {
            // ensures it's a singleton
        }
    }
}
