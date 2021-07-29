// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc
{
    /// <summary>Each instance of IceEncoding represents a version of the Ice encoding supported by this IceRPC runtime.
    /// </summary>
    public sealed class IceEncoding
    {
        public static readonly IceEncoding V11 = new(EncodingNames.V11);
        public static readonly IceEncoding V20 = new(EncodingNames.V20);

        public static IceEncoding Parse(string s) =>
            TryParse(s, out IceEncoding? iceEncoding) ? iceEncoding :
                throw new FormatException($"'{s}' does not represent a supported version of the Ice encoding");

        public static bool TryParse(string s, [NotNullWhen(true)] out IceEncoding? iceEncoding)
        {
            switch (s)
            {
                case EncodingNames.V11:
                    iceEncoding = V11;
                    return true;

                case EncodingNames.V20:
                    iceEncoding = V20;
                    return true;

                default:
                    iceEncoding = null;
                    return false;
            }
        }

        private readonly string _name;

        public override string ToString() => _name;

        private IceEncoding(string name) => _name = name;
    }
}
