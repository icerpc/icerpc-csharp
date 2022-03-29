// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>The base class for Slice encodings supported by this IceRPC runtime.</summary>
    public abstract class SliceEncoding : IEquatable<SliceEncoding>
    {
        /// <summary>Version 1.1 of the Slice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        public static readonly SliceEncoding Slice11 = Slice11Encoding.Instance;

        /// <summary>Version 2.0 of the Slice encoding, supported by IceRPC.</summary>
        public static readonly SliceEncoding Slice20 = Slice20Encoding.Instance;

        /// <summary>The name of this encoding, for example "2.0" for the Slice 2.0 encoding.</summary>
        public string Name { get; }

        private protected const string Slice11Name = "1.1";
        private protected const string Slice20Name = "2.0";

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

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(SliceEncoding? lhs, SliceEncoding? rhs) =>
            ReferenceEquals(lhs, rhs) || (lhs?.Equals(rhs) ?? false);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(SliceEncoding? lhs, SliceEncoding? rhs) => !(lhs == rhs);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is SliceEncoding value && Equals(value);

        /// <summary>Checks if this encoding is equal to another encoding.</summary>
        /// <param name="other">The other encoding.</param>
        /// <returns><c>true</c>when the two encodings have the same name; otherwise, <c>false</c>.</returns>
        public bool Equals(SliceEncoding? other) => Name == other?.Name;

        /// <summary>Computes the hash code for this encoding.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        private protected SliceEncoding(string name) => Name = name;
    }
}
