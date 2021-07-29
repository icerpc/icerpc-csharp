// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Globalization;

namespace IceRpc
{
    /// <summary>...</summary>
    public sealed class Encoding : IEquatable<Encoding>
    {
        /// <summary>Version 1.1 of the Ice encoding, supported by Ice but not by IceRPC.</summary>
        public static readonly Encoding Ice10 = new(EncodingNames.V10);

        /// <summary>Version 1.1 of the Ice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        public static readonly Encoding Ice11 = new(EncodingNames.V11);

        /// <summary>Version 2.0 of the Ice encoding, supported by IceRPC.</summary>
        public static readonly Encoding Ice20 = new(EncodingNames.V20);

        /// <summary>An unknown encoding, used as the default payload encoding for unknown protocols.</summary>
        internal static readonly Encoding Unknown = new(EncodingNames.Unknown);

        private readonly string _name;

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
        public bool Equals(Encoding? other) => _name == other?._name;

        /// <inheritdoc/>
        public override int GetHashCode() => _name.GetHashCode(StringComparison.Ordinal);

        public static Encoding FromMajorMinor(byte major, byte minor) => // TODO make internal
            (major, minor) switch
            {
                (1, 0) => Ice10,
                (1, 1) => Ice11,
                (2, 0) => Ice20,
                _ => new Encoding($"{major}.{minor}")
            };

        public static Encoding FromString(string name) =>
            name switch
            {
                EncodingNames.V10 => Ice10,
                EncodingNames.V11 => Ice11,
                EncodingNames.V20 => Ice20,
                EncodingNames.Unknown => Unknown,
                _ => new Encoding(name)
            };

        /// <inheritdoc/>
        public override string ToString() => _name;

        internal (byte Major, byte Minor) ToMajorMinor()
        {
            switch (_name)
            {
                case EncodingNames.V10:
                    return ((byte)1, (byte)0);

                case EncodingNames.V11:
                    return ((byte)1, (byte)1);

                case EncodingNames.V20:
                    return ((byte)2, (byte)0);

                default:
                    if (_name.Length == 3 && _name[1] == '.')
                    {
                        try
                        {
                            byte major = byte.Parse(_name.AsSpan(0, 1));
                            byte minor = byte.Parse(_name.AsSpan(2, 1));
                            return (major, minor);
                        }
                        catch (FormatException ex)
                        {
                            throw new NotSupportedException(
                                $"cannot convert encoding '{this}' into to major/minor bytes", ex);
                        }
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"cannot convert encoding '{this}' into to major/minor bytes");
                    }
            }
        }
        private Encoding(string name) =>
            _name = name;
    }

    internal static class EncodingExtensions
    {
        internal static void CheckSupportedIceEncoding(this Encoding encoding)
        {
            if (encoding != Encoding.Ice11 && encoding != Encoding.Ice20)
            {
                throw new NotSupportedException($"encoding '{encoding}' is not a supported by this IceRPC runtime");
            }
        }
    }
}
