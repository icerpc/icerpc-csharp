// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    /// <summary>Provides a typed holder for a transport name.</summary>
    public readonly struct TransportId : IEquatable<TransportId>
    {
        /// <summary>The transport's name, for example "tcp".</summary>
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

        /// <summary>Converts implicitely a string (transport name) into a transport ID.</summary>
        /// <param name="name">The transport name.</param>
        /// <returns>The corresponding transport ID.</returns>
        public static implicit operator TransportId(string name) => FromString(name);

        /// <summary>Creates a transport ID from a string (transport name).</summary>
        /// <param name="name">The transport name.</param>
        /// <returns>The corresponding transport ID.</returns>
        public static TransportId FromString(string name) => new TransportId(name);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TransportId value && this.Equals(value);

        /// <inheritdoc/>
        public bool Equals(TransportId other) => Name == other.Name;

        /// <inheritdoc/>
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        /// <inheritdoc/>
        public override string ToString() => Name;

        // If name has a corresponding interned string, use the interned string for Name, otherwise use name as-is.
        private TransportId(string name) => Name = string.IsInterned(name) ?? name;
    }
}
