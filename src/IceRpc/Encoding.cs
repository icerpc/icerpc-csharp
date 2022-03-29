// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;

namespace IceRpc
{
    /// <summary>Encoding identifies the format (a.k.a. encoding) used to encode data into bytes. With IceRPC, it is
    /// usually the Slice 2.0 encoding named "2.0".</summary>
    public class Encoding : IEquatable<Encoding>
    {
        /// <summary>The name of this encoding, for example "2.0" for the Slice 2.0 encoding.</summary>
        public string Name { get; }

        private protected const string Slice11Name = "1.1";
        private protected const string Slice20Name = "2.0";

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Encoding? lhs, Encoding? rhs) =>
            ReferenceEquals(lhs, rhs) || (lhs?.Equals(rhs) ?? false);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Encoding? lhs, Encoding? rhs) => !(lhs == rhs);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Encoding value && Equals(value);

        /// <summary>Checks if this encoding is equal to another encoding.</summary>
        /// <param name="other">The other encoding.</param>
        /// <returns><c>true</c>when the two encodings have the same name; otherwise, <c>false</c>.</returns>
        public bool Equals(Encoding? other) => Name == other?.Name;

        /// <summary>Computes the hash code for this encoding.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        /// <summary>Converts this encoding into a string.</summary>
        /// <returns>The name of the encoding.</returns>
        public override string ToString() => Name;

        private protected Encoding(string name) => Name = name;
    }
}
