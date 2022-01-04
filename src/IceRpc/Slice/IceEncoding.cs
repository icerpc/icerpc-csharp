// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>The base class for Slice encodings supported by this IceRPC runtime.</summary>
    public abstract class IceEncoding : Encoding
    {
        /// <summary>Returns a supported Slice encoding with the given name.</summary>
        /// <param name="name">The name of the encoding.</param>
        /// <returns>A supported Slice encoding.</returns>
        public static new IceEncoding FromString(string name) =>
            name switch
            {
                Slice11Name => Slice11,
                Slice20Name => Slice20,
                _ => throw new ArgumentException($"{name} is not the name of a supported Slice encoding", nameof(name))
            };

        private protected IceEncoding(string name)
            : base(name)
        {
        }
    }
}
