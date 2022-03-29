// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>The base class for Slice encodings supported by this IceRPC runtime.</summary>
    public abstract class SliceEncoding : Encoding
    {
        /// <summary>Version 1.1 of the Slice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        public static readonly SliceEncoding Slice11 = Slice11Encoding.Instance;

        /// <summary>Version 2.0 of the Slice encoding, supported by IceRPC.</summary>
        public static readonly SliceEncoding Slice20 = Slice20Encoding.Instance;

        /// <summary>Returns a supported Slice encoding with the given name.</summary>
        /// <param name="name">The name of the encoding.</param>
        /// <returns>A supported Slice encoding.</returns>
        public static SliceEncoding FromString(string name) =>
            name switch
            {
                Slice11Name => Slice11,
                Slice20Name => Slice20,
                _ => throw new ArgumentException($"{name} is not the name of a supported Slice encoding", nameof(name))
            };

        private protected SliceEncoding(string name)
            : base(name)
        {
        }
    }
}
