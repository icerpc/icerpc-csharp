// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>This attribute class is used by the generated code to mark Slice-defined operations that can be called from
/// <see cref="Service.DispatchAsync" />.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SliceOperationAttribute : Attribute
{
    /// <summary>Gets the operation name.</summary>
    /// <value>The operation name.</value>
    public string Value { get; }

    /// <summary>Constructs a Slice operation attribute.</summary>
    /// <param name="value">The operation name.</param>>
    public SliceOperationAttribute(string value) => Value = value;
}
