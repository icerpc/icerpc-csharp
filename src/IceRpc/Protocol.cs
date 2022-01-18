// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
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

        /// <summary>Checks if <paramref name="fragment"/> contains only unreserved characters, reserved characters or
        /// '%'.</summary>
        /// <param name="fragment">The path to check.</param>
        /// <exception cref="FormatException">Thrown if the fragment is not valid.</exception>
        /// <remarks>The fragment of a URI with a supported protocol satisfies these requirements.</remarks>
        internal static void CheckFragment(string fragment)
        {
            if (!IsValid(fragment, "\"<>\\^`{|}"))
            {
                throw new FormatException(
                    @$"invalid fragment '{fragment
                    }'; a valid fragment contains only unreserved characters, reserved characters or '%'");
            }
        }

        /// <summary>Checks if <paramref name="path"/> starts with a <c>/</c> and contains only unreserved characters,
        /// <c>%</c>, or reserved characters other than <c>?</c> and <c>#</c>.</summary>
        /// <param name="path">The path to check.</param>
        /// <exception cref="FormatException">Thrown if the path is not valid.</exception>
        /// <remarks>The absolute path of a URI with a supported protocol satisfies these requirements.</remarks>
        internal static void CheckPath(string path)
        {
            if (path.Length == 0 || path[0] != '/' || !IsValid(path, "\"<>#?\\^`{|}"))
            {
                throw new FormatException(
                    @$"invalid path '{path
                    }'; a valid path starts with '/' and contains only unreserved characters, '%' or reserved characters other than '?' and '#'");
            }
        }

        /// <summary>Returns the Slice encoding that this protocol uses for its headers.</summary>
        /// <returns>The Slice encoding.</returns>
        internal virtual IceEncoding? SliceEncoding => null;

        /// <summary>Checks if a properly escaped URI absolute path is valid for this protocol.</summary>
        /// <param name="uriPath">The absolute path to check.</param>
        /// <exception cref="FormatException">Thrown if the path is not valid.</exception>
        internal virtual void CheckUriPath(string uriPath) =>
            // by default, any URI path is ok
            Debug.Assert(IsSupported || this == Relative); // should not be called for other protocols

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

        internal byte ToByte() => Name switch
        {
            IceName => 1,
            IceRpcName => 2,
            _ => throw new NotSupportedException($"cannot convert protocol '{Name}' into a byte")
        };

        /// <summary>Checks if <paramref name="s"/> contains only printable ASCII characters other than space (x20) and
        /// the characters included held by the <paramref name="invalidChars"/> string.</summary>
        private protected static bool IsValid(string s, string invalidChars)
        {
            foreach (char c in s)
            {
                if (c.CompareTo('\x20') <= 0 ||
                    c.CompareTo('\x7F') >= 0 ||
                    invalidChars.Contains(c, StringComparison.InvariantCulture))
                {
                    return false;
                }
            }
            return true;
        }

        private protected Protocol(string name) => Name = name;
    }
}
