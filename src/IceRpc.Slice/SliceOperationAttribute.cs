// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Slice;

/// <summary>Represents an attribute that the Slice compiler uses to mark helper methods it generates on Service
/// interfaces.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SliceOperationAttribute : Attribute
{
    /// <summary>Gets the operation name.</summary>
    /// <value>The operation name.</value>
    public string Value { get; }

    /// <summary>Constructs a Slice operation attribute.</summary>
    /// <param name="value">The operation name.</param>>
    public SliceOperationAttribute(string value) => Value = value;
}
