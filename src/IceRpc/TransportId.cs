// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    public readonly struct TransportId : IEquatable<TransportId>
    {
        public readonly string Name;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(TransportId lhs, TransportId rhs) => lhs.Equals(rhs);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(TransportId lhs, TransportId rhs) => !lhs.Equals(rhs);

        public static implicit operator TransportId(string s) => FromString(s);

        public static TransportId FromString(string s) => new TransportId(s);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TransportId value && this.Equals(value);

        /// <inheritdoc/>
        public bool Equals(TransportId other) => Name.Equals(other.Name);

        /// <inheritdoc/>
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        /// <inheritdoc/>
        public override string ToString() => Name;

        private TransportId(string name) => Name = string.IsInterned(name) ?? name;
    }
}
