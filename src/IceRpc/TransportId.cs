// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace IceRpc
{
    public sealed class TransportId : IEquatable<TransportId>
    {
        private static readonly IReadOnlyDictionary<string, TransportId> _wellKnownIds =
            new[] { "coloc", "quic", "loc", "ssl", "tcp", "udp" }.ToDictionary(
                name => name,
                name => new TransportId(name, wellKnown: true));

        public string Name { get; }
        private readonly bool _isWellKnown;

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

        public static TransportId FromString(string s) =>
            _wellKnownIds.TryGetValue(s, out TransportId? transportId) ? transportId : new TransportId(s);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is TransportId value && this.Equals(value);

        /// <inheritdoc/>
        public bool Equals(TransportId? other) => _isWellKnown ? base.Equals(other) : Name == other?.Name;

        /// <inheritdoc/>
        public override int GetHashCode() =>
            _isWellKnown ? base.GetHashCode() : Name.GetHashCode(StringComparison.Ordinal);

        /// <inheritdoc/>
        public override string ToString() => Name;

        private TransportId(string name, bool wellKnown = false)
        {
            Name = name;
            _isWellKnown = wellKnown;
        }
    }
}
