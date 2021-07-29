// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Globalization;

namespace IceRpc
{
    /// <summary>...</summary>
    public partial class Encoding : global::System.IEquatable<Encoding>
    {
        public static readonly Encoding V10 = new(EncodingNames.V10);

        /// <summary>Version 1.1 of the Ice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        public static readonly Encoding V11 = new(IceEncoding.V11);

        /// <summary>Version 2.0 of the Ice encoding, supported by IceRPC.</summary>
        public static readonly Encoding V20 = new(IceEncoding.V20);

        /// <summary>The major version number of this version of the Ice encoding.</summary>
        public byte Major => byte.Parse(ToString().AsSpan(0, 1));

        /// <summary>The minor version number of this version of the Ice encoding.</summary>
        public byte Minor => byte.Parse(ToString().AsSpan(2, 1));

        private readonly string _name;
        private readonly IceEncoding? _iceEncoding;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Encoding? lhs, Encoding? rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs is null || rhs is null)
            {
                return false;
            }
            return rhs.Equals(lhs);
        }

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Encoding? lhs, Encoding? rhs) => !(lhs == rhs);

        /// <summary>Constructs a new instance of <see cref="Encoding"/>.</summary>
        /// <param name="major">The major version number of this version of the Ice encoding.</param>
        /// <param name="minor">The minor version number of this version of the Ice encoding.</param>
        public Encoding(byte major, byte minor)
            : this(IceEncoding.Parse($"{major}.{minor}"))
        {
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Encoding value && Equals(value);

        /// <inheritdoc/>
        public bool Equals(Encoding? other) =>
            other != null &&
            ((_iceEncoding != null && _iceEncoding == other._iceEncoding) || _name == other._name);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            _iceEncoding?.GetHashCode() ?? _name.GetHashCode(StringComparison.Ordinal);

        internal bool IsSupported => this == V11 || this == V20;

        public IceEncoding ToIceEncoding() =>
            _iceEncoding ?? throw new NotSupportedException($"'{_name}' is not a supported Ice encoding");

        /// <summary>Parses a string into an Encoding.</summary>
        /// <param name="s">The string to parse.</param>
        /// <returns>A new encoding.</returns>
        public static Encoding Parse(string s) => FromString(s);
        public static Encoding FromString(string name) =>
            IceEncoding.TryParse(name, out IceEncoding? iceEncoding) ? new Encoding(iceEncoding) : new Encoding(name);

        /// <inheritdoc/>
        public override string ToString() => _name;

        private Encoding(string name) =>
            _name = name;

        private Encoding(IceEncoding iceEncoding)
        {
            _iceEncoding = iceEncoding;
            _name = iceEncoding.ToString();
        }
    }
}
