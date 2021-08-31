// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;

namespace IceRpc
{
    /// <summary>Encoding identifies the format (a.k.a. encoding) used to encode data into bytes. With IceRPC, it is
    /// usually the Ice 2.0 encoding named "2.0".</summary>
    public class Encoding : IEquatable<Encoding>
    {
        /// <summary>Version 1.0 of the Ice encoding, supported by Ice but not by IceRPC.</summary>
        public static readonly Encoding Ice10 = new(Ice10Name);

        /// <summary>Version 1.1 of the Ice encoding, supported by IceRPC and Ice 3.5 or greater.</summary>
        public static readonly Ice11Encoding Ice11 = Ice11Encoding.Instance;

        /// <summary>Version 2.0 of the Ice encoding, supported by IceRPC.</summary>
        public static readonly Ice20Encoding Ice20 = Ice20Encoding.Instance;

        /// <summary>The name of this encoding, for example "2.0" for the Ice 2.0 encoding.</summary>
        public string Name { get; }

        /// <summary>An unknown encoding, used as the default payload encoding for unsupported protocols.</summary>
        internal static readonly Encoding Unknown = new(UnknownName);

        private protected const string Ice11Name = "1.1";
        private protected const string Ice20Name = "2.0";
        private const string Ice10Name = "1.0";
        private const string UnknownName = "unknown";

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
            return lhs.Equals(rhs);
        }

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Encoding? lhs, Encoding? rhs) => !(lhs == rhs);

        /// <summary>Returns an Encoding with the given name. This method always succeeds.</summary>
        /// <param name="name">The name of the encoding.</param>
        /// <returns>One of the well-known Encoding instance (Ice11, Ice20 etc.) when the name matches; otherwise, a new
        /// Encoding instance.</returns>
        public static Encoding FromString(string name) =>
            name switch
            {
                Ice10Name => Ice10,
                Ice11Name => Ice11,
                Ice20Name => Ice20,
                UnknownName => Unknown,
                _ => new Encoding(name)
            };

        /// <summary>Creates an empty payload encoded with this encoding.</summary>
        /// <returns>A new empty payload.</returns>
        public virtual ReadOnlyMemory<ReadOnlyMemory<byte>> CreateEmptyPayload() =>
            throw new NotSupportedException($"encoding '{this}' is not a supported by this IceRPC runtime");

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

        /// <summary>Returns an Encoding with the given major and minor versions. This method always succeeds.
        /// </summary>
        internal static Encoding FromMajorMinor(byte major, byte minor) =>
            (major, minor) switch
            {
                (1, 0) => Ice10,
                (1, 1) => Ice11,
                (2, 0) => Ice20,
                _ => new Encoding($"{major}.{minor}")
            };

        internal virtual IIceDecoderFactory<IceDecoder> GetIceDecoderFactory(
            FeatureCollection features,
            DefaultIceDecoderFactories defaultIceDecoderFactories) =>
            throw new NotSupportedException($"cannot create an Ice decoder for encoding '{this}'");

        /// <summary>Creates an Ice encoder for this encoding.</summary>
        /// <param name="bufferWriter">The buffer writer.</param>
        /// <returns>A new encoder for the specified Ice encoding.</returns>
        internal virtual IceEncoder CreateIceEncoder(BufferWriter bufferWriter) =>
            throw new NotSupportedException($"cannot create Ice encoder for encoding '{this}'");

        /// <summary>Returns the major and minor byte versions of this encoding.</summary>
        /// <exception name="NotSupportedException">Thrown when this encoding's name is not in the major.minor format.
        /// </exception>
        internal (byte Major, byte Minor) ToMajorMinor()
        {
            switch (Name)
            {
                case Ice10Name:
                    return ((byte)1, (byte)0);

                case Ice11Name:
                    return ((byte)1, (byte)1);

                case Ice20Name:
                    return ((byte)2, (byte)0);

                default:
                    if (Name.Length == 3 && Name[1] == '.')
                    {
                        try
                        {
                            byte major = byte.Parse(Name.AsSpan(0, 1));
                            byte minor = byte.Parse(Name.AsSpan(2, 1));
                            return (major, minor);
                        }
                        catch (FormatException ex)
                        {
                            throw new NotSupportedException(
                                $"cannot convert encoding '{this}' to major/minor bytes", ex);
                        }
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"cannot convert encoding '{this}' to major/minor bytes");
                    }
            }
        }

        private protected Encoding(string name) => Name = name;
    }
}
