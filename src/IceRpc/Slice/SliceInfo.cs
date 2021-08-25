// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Slice
{
    /// <summary>SliceInfo encapsulates the details of a slice for an unknown class or remote exception encoded with
    /// the Ice 1.1 encoding.</summary>
    public sealed class SliceInfo
    {
        /// <summary>The Slice type ID or compact ID for this slice.</summary>
        public string TypeId { get; }

        /// <summary>The encoded bytes for this slice, including the leading size integer.</summary>
        public ReadOnlyMemory<byte> Bytes { get; }

        /// <summary>The class instances referenced by this slice.</summary>
        public IReadOnlyList<AnyClass> Instances { get; internal set; }

        /// <summary>Whether or not the slice contains tagged members.</summary>
        public bool HasTaggedMembers { get; }

        internal SliceInfo(
            string typeId,
            ReadOnlyMemory<byte> bytes,
            IReadOnlyList<AnyClass> instances,
            bool hasTaggedMembers)
        {
            TypeId = typeId;
            Bytes = bytes;
            Instances = instances;
            HasTaggedMembers = hasTaggedMembers;
        }
    }
}
