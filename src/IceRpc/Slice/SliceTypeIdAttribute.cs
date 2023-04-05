// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>This attribute class is used by the generated code to assign a Slice type ID to C# classes, interfaces
/// and structs mapped from Slice interfaces, classes and exceptions. </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, Inherited = false)]
public sealed class SliceTypeIdAttribute : Attribute
{
    /// <summary>Gets the Slice type ID.</summary>
    /// <value>The Slice type ID string.</value>
    public string Value { get; }

    /// <summary>Constructs a Slice type ID attribute.</summary>
    /// <param name="value">The Slice type ID.</param>>
    public SliceTypeIdAttribute(string value) => Value = value;
}

/// <summary>This attribute class is used by the generated code to assign a compact Slice type ID to C# classes
///  mapped from Slice classes. </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CompactSliceTypeIdAttribute : Attribute
{
    /// <summary>Gets the compact Slice type ID.</summary>
    /// <value>The compact Slice type ID numeric value.</value>
    public int Value { get; }

    /// <summary>Constructs a compact Slice type ID attribute.</summary>
    /// <param name="value">The compact type ID.</param>>
    public CompactSliceTypeIdAttribute(int value) => Value = value;
}
