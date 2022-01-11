// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Globalization;

namespace IceRpc
{
    /// <summary>The URI scheme of a proxy or endpoint.</summary>
    public class Scheme : IEquatable<Scheme>
    {
        /// <summary>The <c>ice</c> scheme, which is also a <see cref="Protocol"/>.</summary>
        public static Protocol Ice => IceProtocol.Instance;

        /// <summary>The <c>icerpc</c> scheme, which is also a <see cref="Protocol"/>.</summary>
        public static Protocol IceRpc => IceRpcProtocol.Instance;

        /// <summary>The name of this scheme.</summary>
        public string Name { get; }

        private protected const string IceName = "ice";
        private protected const string IceRpcName = "icerpc";

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Scheme? lhs, Scheme? rhs)
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
        public static bool operator !=(Scheme? lhs, Scheme? rhs) => !(lhs == rhs);

        /// <summary>Returns a scheme with the given name. This method always succeeds.</summary>
        /// <param name="name">The name of the scheme.</param>
        /// <returns>A scheme with the given name.</returns>
        public static Scheme FromString(string name) => name switch
        {
            IceName => Ice,
            IceRpcName => IceRpc,
            _ => new Scheme(name)
        };

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Scheme value && Equals(value);

        /// <summary>Checks if this scheme is equal to another scheme.</summary>
        /// <param name="other">The other scheme.</param>
        /// <returns><c>true</c>when the two schemes have the same name; otherwise, <c>false</c>.</returns>
        public bool Equals(Scheme? other) => Name == other?.Name;

        /// <summary>Computes the hash code for this scheme.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        /// <summary>Converts this scheme into a string.</summary>
        /// <returns>The name of the scheme.</returns>
        public override string ToString() => Name;

        internal static Scheme FromByte(byte protocolMajor) => protocolMajor switch
        {
            1 => Ice,
            2 => IceRpc,
            _ => throw new NotSupportedException($"cannot convert '{protocolMajor}.0' into a scheme")
        };

        internal byte ToByte() => Name switch
        {
            IceName => 1,
            IceRpcName => 2,
            _ => throw new NotSupportedException($"cannot convert scheme '{Name}' into a byte")
        };

        private protected Scheme(string name) => Name = name;
    }
}
