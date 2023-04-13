// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Encapsulates the details of a class slice (as in slice of cake) that an <see cref="IActivator" /> could
/// not decode.</summary>
public sealed class SliceInfo
{
    /// <summary>Gets the Slice type ID or compact ID for this slice.</summary>
    public string TypeId { get; }

    /// <summary>Gets the encoded bytes for this slice, including the leading size integer.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Gets a value indicating whether or not the slice contains tagged fields.</summary>
    public bool HasTaggedFields { get; }

    /// <summary>Gets the class instances referenced by this slice.</summary>
    public IReadOnlyList<SliceClass> Instances { get; internal set; }

    internal SliceInfo(
        string typeId,
        ReadOnlyMemory<byte> bytes,
        IReadOnlyList<SliceClass> instances,
        bool hasTaggedFields)
    {
        TypeId = typeId;
        Bytes = bytes;
        Instances = instances;
        HasTaggedFields = hasTaggedFields;
    }
}
