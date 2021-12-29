// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc
{
    /// <summary>A proxy parser for the ZeroC Ice stringified proxy format. For example, it parses
    /// "fred:tcp -h localhost -p 10000".</summary>
    public class IceProxyParser : IProxyParser
    {
        /// <summary>The instance of the IceProxyParser.</summary>
        public static IProxyParser Instance { get; } = new IceProxyParser();

        /// <inheritdoc/>
        public Proxy Parse(string s, IInvoker? invoker = null)
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("an empty string does not represent a proxy");
            }

            // TODO: refactor
            Proxy proxy = Ice1Parser.ParseProxyString(proxyString);
            proxy.Invoker = invoker;
            return proxy;
        }

        private IceProxyParser()
        {
            // ensures it's a singleton
        }
    }
}
