// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc
{
    /// <summary>Protocol identifies a RPC protocol supported by this IceRPC runtime.</summary>
    public abstract class Protocol : Scheme
    {
        /// <summary>Specifies whether or not the protocol supports fields in protocol frame headers.</summary>
        /// <returns><c>true</c> if the protocol supports fields; otherwise, <c>false</c>.</returns>
        public abstract bool HasFieldSupport { get; }

        /// <summary>Returns the Slice encoding that this protocol uses for its headers.</summary>
        /// <returns>The Slice encoding.</returns>
        internal abstract IceEncoding SliceEncoding { get; }

        private protected Protocol(string name)
            : base(name)
        {
        }
    }
}
