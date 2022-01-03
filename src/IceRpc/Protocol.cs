// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Globalization;

namespace IceRpc
{
    /// <summary>Protocol identifies the protocol used by IceRpc connections.</summary>
    public class Protocol : IEquatable<Protocol>
    {
        /// <summary>The protocol supported by all Ice versions since Ice 1.0.</summary>
        public static readonly Protocol Ice = IceProtocol.Instance;

        /// <summary>The protocol introduced in IceRpc.</summary>
        public static readonly Protocol IceRpc = IceRpcProtocol.Instance;

        /// <summary>The protocol code of this protocol.</summary>
        public ProtocolCode Code { get; }

        /// <summary>The name of this protocol, for example "icerpc" for the IceRPC protocol.</summary>
        public string Name { get; }

        /// <summary>Returns the Ice encoding that this protocol uses for its headers. It's also used as the
        /// default encoding for endpoints that don't explicitly specify an encoding.</summary>
        /// <returns>The Ice encoding, or null if the protocol does not use a known Ice encoding.</returns>
        internal virtual IceEncoding? IceEncoding => null;

        /// <summary>Specifies whether or not the protocol supports fields in protocol frame
        /// headers.</summary>
        /// <returns><c>true</c> if the protocol supports fields.</returns>
        internal virtual bool HasFieldSupport => false;

        private protected const string IceName = "ice";
        private protected const string IceRpcName = "icerpc";

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Protocol? lhs, Protocol? rhs)
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
        public static bool operator !=(Protocol? lhs, Protocol? rhs) => !(lhs == rhs);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Protocol value && Equals(value);

        /// <summary>Checks if this encoding is equal to another encoding.</summary>
        /// <param name="other">The other encoding.</param>
        /// <returns><c>true</c>when the two encodings have the same name; otherwise, <c>false</c>.</returns>
        public bool Equals(Protocol? other) => Code == other?.Code;

        /// <summary>Computes the hash code for this encoding.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => Code.GetHashCode();

        /// <summary>Converts this encoding into a string.</summary>
        /// <returns>The name of the encoding.</returns>
        public override string ToString() => Name;

        /// <summary>Returns a Protocol for the given <see cref="ProtocolCode"/>. This method always succeeds.
        /// </summary>
        /// <param name="code">The protocol code.</param>
        public static Protocol FromProtocolCode(ProtocolCode code) =>
            code switch
            {
                ProtocolCode.Ice1 => Ice,
                ProtocolCode.Ice2 => IceRpc,
                _ => new Protocol(code, ((byte)code).ToString(CultureInfo.InvariantCulture))
            };

        /// <summary>Returns a Protocol with the given name.</summary>
        /// <param name="name">The name of the protocol.</param>
        /// <returns>The parsed protocol.</returns>
        /// <exception cref="FormatException">Thrown if the protocol name is invalid.</exception>
        public static Protocol Parse(string name)
        {
            return name switch
            {
                IceName => Ice,
                IceRpcName => IceRpc,
                _ => Core(name)
            };

            static Protocol Core(string name)
            {
                if (byte.TryParse(name, out byte value))
                {
                    return FromProtocolCode((ProtocolCode)value);
                }
                else
                {
                    throw new FormatException($"invalid protocol '{name}'");
                }
            }
        }

        private protected Protocol(ProtocolCode code, string name)
        {
            Code = code;
            Name = name;
        }
    }
}
