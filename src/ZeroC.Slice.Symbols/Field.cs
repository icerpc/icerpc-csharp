// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>Represents a field in a Slice definition.</summary>
/// <remarks>Slice fields are used in various contexts, such as structs, enumerations, operation parameters, and return
/// values.</remarks>
public class Field : Entity
{
    /// <summary>Gets the tag for this field, it is <see langword="null"/> for non tagged fields.</summary>
    public required int? Tag { get; init; }

    /// <summary>Gets the field's type.</summary>
    public required TypeRef DataType { get; init; }

    /// <summary>Gets a value indicating whether this field's type is optional.</summary>
    public required bool DataTypeIsOptional { get; init; }

    /// <summary>Gets a value indicating whether this field is tagged. When <see langword="true"/>, the
    /// <see cref="Tag"/> property has a value; otherwise, it is <see langword="null"/>.</summary>
    public bool IsTagged => Tag.HasValue;
}
