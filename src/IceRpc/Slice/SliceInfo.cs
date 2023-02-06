// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>SliceInfo encapsulates the details of a slice for an unknown class encoded with the Slice1
/// encoding.</summary>
public sealed class SliceInfo
{
    /// <summary>Gets the Slice type ID or compact ID for this slice.</summary>
    public string TypeId { get; }

    /// <summary>Gets the encoded bytes for this slice, including the leading size integer.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Gets the class instances referenced by this slice.</summary>
    public IReadOnlyList<SliceClass> Instances { get; internal set; }

    /// <summary>Gets a value indicating whether or not the slice contains tagged members.</summary>
    public bool HasTaggedMembers { get; }

    internal SliceInfo(
        string typeId,
        ReadOnlyMemory<byte> bytes,
        IReadOnlyList<SliceClass> instances,
        bool hasTaggedMembers)
    {
        TypeId = typeId;
        Bytes = bytes;
        Instances = instances;
        HasTaggedMembers = hasTaggedMembers;
    }
}
