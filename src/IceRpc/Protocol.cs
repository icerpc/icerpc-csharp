// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Protocol identifies a RPC protocol.</summary>
    public class Protocol : IEquatable<Protocol>
    {
        /// <summary>The <c>ice</c> protocol.</summary>
        public static Protocol Ice => IceProtocol.Instance;

        /// <summary>The <c>icerpc</c> protocol.</summary>
        public static Protocol IceRpc => IceRpcProtocol.Instance;

        /// <summary>The protocol of relative proxies.</summary>
        public static Protocol Relative { get; } = new(RelativeName);

        /// <summary>Returns the default port for this protocol.</summary>
        /// <value>The value is either -1 (no default port) or between 0 and 65,535.</value>
        public virtual int DefaultUriPort => -1;

        /// <summary>Returns whether or not this protocol supports fields.</summary>
        /// <returns><c>true</c> if the protocol supports fields; otherwise, <c>false</c>.</returns>
        public virtual bool HasFields => false;

        /// <summary>Returns whether or not this protocol supports fragments in proxies.</summary>
        /// <returns><c>true</c> if the protocol supports fragments; otherwise, <c>false</c>.</returns>
        public virtual bool HasFragment => false;

        /// <summary>Checks if IceRPC can an establish a connection using this protocol.</summary>
        /// <returns><c>true</c> if the protocol is supported; otherwise, <c>false</c>.</returns>
        public virtual bool IsSupported => false;

        /// <summary>The name of this protocol.</summary>
        public string Name { get; }

        /// <summary>Returns the Slice encoding that this protocol uses for its headers.</summary>
        /// <returns>The Slice encoding.</returns>
        internal virtual IceEncoding? SliceEncoding => null;

        internal const string IceName = "ice";
        internal const string IceRpcName = "icerpc";

        private protected const string RelativeName = "";

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Protocol? lhs, Protocol? rhs) =>
            ReferenceEquals(lhs, rhs) || (lhs?.Equals(rhs) ?? false);

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Protocol? lhs, Protocol? rhs) => !(lhs == rhs);

        /// <summary>Returns a protocol with the given name. This method always succeeds.</summary>
        /// <param name="name">The name of the protocol.</param>
        /// <returns>A protocol with the given name in lowercase.</returns>
        public static Protocol FromString(string name)
        {
            name = name.ToLowerInvariant();

            return name switch
            {
                IceName => Ice,
                IceRpcName => IceRpc,
                RelativeName => Relative,
                _ => new Protocol(name)
            };
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Protocol value && Equals(value);

        /// <summary>Checks if this protocol is equal to another protocol.</summary>
        /// <param name="other">The other protocol.</param>
        /// <returns><c>true</c>when the two protocols have the same name; otherwise, <c>false</c>.</returns>
        public bool Equals(Protocol? other) => Name == other?.Name;

        /// <summary>Computes the hash code for this protocol.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        /// <summary>Converts this protocol into a string.</summary>
        /// <returns>The name of the protocol.</returns>
        public override string ToString() => Name;

        internal static Protocol FromByte(byte protocolMajor) => protocolMajor switch
        {
            1 => Ice,
            2 => IceRpc,
            _ => throw new NotSupportedException($"cannot convert '{protocolMajor}.0' into a protocol")
        };

        /// <summary>Checks if a path is valid for this protocol.</summary>
        /// <param name="uriPath">The absolute path to check. The caller guarantees it's a valid URI absolute path.
        /// </param>
        /// <exception cref="FormatException">Thrown if the path is not valid.</exception>
        internal virtual void CheckPath(string uriPath) =>
            // by default, any URI absolute path is ok
            Debug.Assert(IsSupported || this == Relative);

        /// <summary>Checks if these proxy parameters are valid for this protocol.</summary>
        /// <param name="proxyParams">The proxy parameters to check.</param>
        /// <exception cref="FormatException">Thrown if the proxy parameters are not valid.</exception>
        /// <remarks>This method does not and should not check if the parameter names and values are properly escaped;
        /// it does not check for the invalid empty and alt-endpoint parameter names either.</remarks>
        internal virtual void CheckProxyParams(ImmutableDictionary<string, string> proxyParams) =>
            // by default, any dictionary is ok
            Debug.Assert(IsSupported || this == Relative);

        internal byte ToByte() => Name switch
        {
            IceName => 1,
            IceRpcName => 2,
            _ => throw new NotSupportedException($"cannot convert protocol '{Name}' into a byte")
        };

        private protected Protocol(string name) => Name = name;
    }
}
