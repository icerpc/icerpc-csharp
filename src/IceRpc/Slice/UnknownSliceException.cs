// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Slice;

/// <summary>Represents a Slice exception that could not be decoded (Slice1 only).</summary>
public sealed class UnknownSliceException : SliceException
{
    /// <summary>Gets the Slice type ID of this unknown Slice exception.</summary>
    public string TypeId { get; }

    /// <summary>Constructs an unknown Slice exception with the provided type ID and message.</summary>
    /// <param name="typeId">The Slice type ID of the exception.</param>
    /// <param name="message">A message that describes the exception.</param>
    public UnknownSliceException(string typeId, string? message = null)
        : base(message) =>
        TypeId = typeId;

    /// <inheritdoc/>
    // We can't encode such an exception because we don't preserve exception slices during decoding.
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void EncodeCore(ref SliceEncoder encoder) => throw new NotSupportedException();
}
