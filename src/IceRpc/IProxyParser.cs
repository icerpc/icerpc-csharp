// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace IceRpc
{
    /// <summary>The abstraction for proxy parsers.</summary>
    public interface IProxyParser
    {
        /// <summary>Creates a proxy from a string and an optional invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The new proxy.</returns>
        Proxy Parse(string s, IInvoker? invoker = null);

        /// <summary>Tries to create a proxy from a string and invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="proxy">The parsed proxy.</param>
        /// <returns><c>true</c> when the string is parsed successfully; otherwise, <c>false</c>.</returns>
        bool TryParse(string s, IInvoker? invoker, [NotNullWhen(true)] out Proxy? proxy)
        {
            try
            {
                proxy = Parse(s, invoker);
                return true;
            }
            catch
            {
                proxy = null;
                return false;
            }
        }
    }
}
