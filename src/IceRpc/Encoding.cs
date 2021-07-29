// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Globalization;

namespace IceRpc
{
    /// <summary>...</summary>
    public partial class Encoding : IEquatable<Encoding>
    {
        public static readonly Encoding V10 = new(EncodingNames.V10);

        /// <summary>Version 1.1 of the Ice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        public static readonly Encoding V11 = new(IceEncoding.V11);

        /// <summary>Version 2.0 of the Ice encoding, supported by IceRPC.</summary>
        public static readonly Encoding V20 = new(IceEncoding.V20);

        /// <summary>An unknown encoding, used as the default payload encoding for unknown protocols.</summary>
        internal static readonly Encoding Unknown = new("unknown");

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

        public static Encoding FromMajorMinor(byte major, byte minor) => // TODO make internal
            (major, minor) switch
            {
                (1, 0) => V10,
                (1, 1) => V11,
                (2, 0) => V20,
                _ => new Encoding($"{major}.{minor}")
            };

        public static Encoding FromString(string name) =>
            IceEncoding.TryParse(name, out IceEncoding? iceEncoding) ? new Encoding(iceEncoding) : new Encoding(name);

        /// <inheritdoc/>
        public override string ToString() => _name;

        internal (byte Major, byte Minor) ToMajorMinor()
        {
            if (_iceEncoding is IceEncoding iceEncoding)
            {
                return iceEncoding == IceEncoding.V11 ? ((byte)1, (byte)1) : ((byte)2, (byte)0);
            }
            else if (_name.Length == 3 && _name[1] == '.')
            {
                try
                {
                    byte major = byte.Parse(_name.AsSpan(0, 1));
                    byte minor = byte.Parse(_name.AsSpan(2, 1));
                    return (major, minor);
                }
                catch (FormatException ex)
                {
                    throw new NotSupportedException($"cannot convert encoding '{this}' into to major/minor bytes", ex);
                }
            }
            else
            {
                throw new NotSupportedException($"cannot convert encoding '{this}' into to major/minor bytes");
            }
        }

        private Encoding(string name) =>
            _name = name;

        private Encoding(IceEncoding iceEncoding)
        {
            _iceEncoding = iceEncoding;
            _name = iceEncoding.ToString();
        }
    }

    internal static class EncodingExtensions
    {
        internal static void CheckSupportedIceEncoding(this Encoding encoding)
        {
            if (encoding != Encoding.V11 && encoding != Encoding.V20)
            {
                throw new NotSupportedException($"encoding '{encoding}' is not a supported by this IceRPC runtime");
            }
        }
    }
}
