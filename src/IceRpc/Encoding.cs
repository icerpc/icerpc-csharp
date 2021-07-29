// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Globalization;

namespace IceRpc
{
    /// <summary>The Ice encoding defines how Slice constructs are marshaled to and later unmarshaled from sequences of bytes.
    /// An Encoding struct holds a version of the Ice encoding.</summary>
    public readonly partial struct Encoding : global::System.IEquatable<Encoding>
    {
        /// <summary>The major version number of this version of the Ice encoding.</summary>
        public readonly byte Major;

        /// <summary>The minor version number of this version of the Ice encoding.</summary>
        public readonly byte Minor;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Encoding lhs, Encoding rhs) => lhs.Equals(rhs);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Encoding lhs, Encoding rhs) => !lhs.Equals(rhs);

        /// <summary>Constructs a new instance of <see cref="Encoding"/>.</summary>
        /// <param name="major">The major version number of this version of the Ice encoding.</param>
        /// <param name="minor">The minor version number of this version of the Ice encoding.</param>
        public Encoding(byte major, byte minor)
        {
            Major = major;
            Minor = minor;
            Initialize();
        }

        /// <summary>Constructs a new instance of <see cref="Encoding"/> from a decoder.</summary>
        public Encoding(IceRpc.IceDecoder decoder)
        {
            this.Major = decoder.DecodeByte();
            this.Minor = decoder.DecodeByte();
            Initialize();
        }

        /// <inheritdoc/>
        public readonly override bool Equals(object? obj) => obj is Encoding value && this.Equals(value);

        /// <inheritdoc/>
        public readonly bool Equals(Encoding other) =>
            this.Major == other.Major &&
            this.Minor == other.Minor;

        /// <inheritdoc/>
        public readonly override int GetHashCode()
        {
            var hash = new global::System.HashCode();
            hash.Add(this.Major);
            hash.Add(this.Minor);
            return hash.ToHashCode();
        }

        /// <summary>Encodes the fields of this struct.</summary>
        public readonly void Encode(IceRpc.IceEncoder encoder)
        {
            encoder.EncodeByte(this.Major);
            encoder.EncodeByte(this.Minor);
        }

        /// <summary>The constructor calls the Initialize partial method after initializing the fields.</summary>
        partial void Initialize();
    }

    // Extends the Slice-defined Encoding struct
    public readonly partial struct Encoding
    {
        // The encodings known to the IceRPC runtime.

        /// <summary>Version 1.0 of the Ice encoding, supported by Ice 1.0 to Ice 3.7.</summary>
        public static readonly Encoding V10 = new(1, 0);

        /// <summary>Version 1.1 of the Ice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        public static readonly Encoding V11 = new(1, 1);

        /// <summary>Version 2.0 of the Ice encoding, supported by IceRPC.</summary>
        public static readonly Encoding V20 = new(2, 0);

        internal bool IsSupported => this == V11 || this == V20;

        public IceEncoding ToIceEncoding() => IceEncoding.Parse(ToString());

        /// <summary>Parses a string into an Encoding.</summary>
        /// <param name="str">The string to parse.</param>
        /// <returns>A new encoding.</returns>
        public static Encoding Parse(string str)
        {
            int pos = str.IndexOf('.', StringComparison.Ordinal);
            if (pos == -1)
            {
                throw new FormatException($"malformed encoding string '{str}'");
            }

            string majStr = str[..pos];
            string minStr = str[(pos + 1)..];
            try
            {
                byte major = byte.Parse(majStr, CultureInfo.InvariantCulture);
                byte minor = byte.Parse(minStr, CultureInfo.InvariantCulture);
                return new Encoding(major, minor);
            }
            catch (FormatException)
            {
                throw new FormatException($"malformed encoding string '{str}'");
            }
        }

        /// <summary>Attempts to parse a string into an Encoding.</summary>
        /// <param name="str">The string to parse.</param>
        /// <param name="encoding">The resulting encoding.</param>
        /// <returns>True if the parsing succeeded and encoding contains the result; otherwise, false.</returns>
        public static bool TryParse(string str, out Encoding encoding)
        {
            try
            {
                encoding = Parse(str);
                return true;
            }
            catch (FormatException)
            {
                encoding = default;
                return false;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Major}.{Minor}";

        internal void CheckSupported()
        {
            if (!IsSupported)
            {
                throw new NotSupportedException(
                    $"Ice encoding '{this}' is not supported by this IceRPC runtime ({Runtime.StringVersion})");
            }
        }
    }
}
