// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>Parser that creates protocol instances.</summary>
    internal static class ProtocolParser
    {
        /// <summary>Parses a protocol string in the stringified proxy format into a Protocol.</summary>
        /// <param name="str">The string to parse.</param>
        /// <returns>The parsed protocol, or throws an exception if the string cannot be parsed.</returns>
        internal static Protocol Parse(string str)
        {
            switch (str)
            {
                case "ice1":
                    return Protocol.Ice1;
                case "ice2":
                    return Protocol.Ice2;
                default:
                    if (str.EndsWith(".0", StringComparison.Ordinal))
                    {
                        str = str[0..^2];
                    }
                    if (byte.TryParse(str, out byte value))
                    {
                        return value > 0 ? (Protocol)value : throw new FormatException("invalid protocol 0");
                    }
                    else
                    {
                        throw new FormatException($"invalid protocol '{str}'");
                    }
            }
        }
    }

    /// <summary>Provides extensions methods for <see cref="Protocol"/>.</summary>
    internal static class ProtocolExtensions
    {
        /// <summary>Checks if this protocol is supported by the IceRPC runtime. If not supported, throws
        /// <see cref="NotSupportedException"/>.</summary>
        /// <param name="protocol">The protocol.</param>
        internal static void CheckSupported(this Protocol protocol)
        {
            if (protocol != Protocol.Ice1 && protocol != Protocol.Ice2)
            {
                throw new NotSupportedException(
                    @$"Ice protocol '{protocol.GetName()
                    }' is not supported by this IceRPC runtime ({typeof(Protocol).Assembly.GetName().Version})");
            }
        }
    }
}
